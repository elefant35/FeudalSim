using System.IO.Hashing;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Social;

[Flags]
public enum RelTags : ushort { None = 0, Acquaintance = 1, Friend = 2, CloseFriend = 4, Rival = 8, Enemy = 16 }

/// <summary>One decaying opinion slot per modifier type per edge (16 §3.1, §4.3). Times in game minutes.</summary>
public struct ModSlot
{
    public ushort Modifier;
    public float Value, Floor;
    public long TimeMin;
    public byte Count;
}

/// <summary>A directed relationship A→B (16 §3.1, §4).</summary>
public sealed class RelationshipEdge
{
    public float Trust, Trust0, Familiarity, PeakFamiliarity, FearEvent;
    public long FearTimeMin, LastContactMin, FriendSinceMin = -1, EnemyCalmSinceMin = -1;
    public RelTags Tags;
    public float SocialContactToday, WorkHoursToday;
    public long SocialContactDay = -1, WorkDay = -1;
    public List<ModSlot> Mods { get; } = [];
}

/// <summary>
/// Relationships (16 §4): Opinion as derived terms + decaying modifier slots, Trust, Familiarity, Fear and tags,
/// stored as sparse directed edges in id order (deterministic). Reads are pure; the daily update (familiarity decay,
/// trust drift, slot cleanup, tags) is the only state change besides events. Derived terms in M1: D3 homeland,
/// D4 faith, D5 values alignment (D1 kin and D2 household need the kin graph; D9 needs reputation — M1-07).
/// </summary>
public sealed class RelationshipStore
{
    public const float MinutesPerDay = 1440f;
    private readonly SimWorld _world;
    private readonly SortedDictionary<(ulong Holder, ulong Other), RelationshipEdge> _edges = [];

    internal RelationshipStore(SimWorld world) => _world = world;

    public int Count => _edges.Count;

    /// <summary>True once Landfall shipmate edges were created (canon: shipmates start at Familiarity 15–35).</summary>
    public bool ShipmatesSeeded { get; internal set; }

    public IEnumerable<KeyValuePair<(ulong Holder, ulong Other), RelationshipEdge>> Edges => _edges;

    public bool TryGet(EntityId holder, EntityId other, out RelationshipEdge edge)
        => _edges.TryGetValue((holder.Value, other.Value), out edge!);

    public RelationshipEdge GetOrCreate(EntityId holder, EntityId other)
    {
        if (_edges.TryGetValue((holder.Value, other.Value), out var e)) { return e; }
        var t0 = TrustBaseline(holder, other);
        e = new RelationshipEdge { Trust = t0, Trust0 = t0, LastContactMin = _world.Clock.GameMinute };
        _edges.Add((holder.Value, other.Value), e);
        return e;
    }

    /// <summary>16 §4.2. Pure: a slot whose decayed magnitude above its floor is below 1 contributes only its floor.</summary>
    public float Opinion(EntityId holder, EntityId other)
    {
        var total = Derived(holder, other);
        if (_edges.TryGetValue((holder.Value, other.Value), out var e))
        {
            var now = _world.Clock.GameMinute;
            var hp = Holder(holder);
            foreach (var slot in e.Mods) { total += Decayed(slot, hp, now, out _); }
        }

        return Math.Clamp(total, -100f, 100f);
    }

    public float Trust(EntityId holder, EntityId other) => _edges.TryGetValue((holder.Value, other.Value), out var e) ? e.Trust : TrustBaseline(holder, other) - 10f;

    public float Familiarity(EntityId holder, EntityId other) => _edges.TryGetValue((holder.Value, other.Value), out var e) ? e.Familiarity : 0f;

    /// <summary>Event fear (16 §4.10), half-life 8 days; derived (power) fear arrives with 17.</summary>
    public float Fear(EntityId holder, EntityId other)
        => _edges.TryGetValue((holder.Value, other.Value), out var e) ? e.FearEvent * Half(_world.Clock.GameMinute - e.FearTimeMin, 8f * MinutesPerDay) : 0f;

    public bool HasTag(EntityId holder, EntityId other, RelTags tag) => _edges.TryGetValue((holder.Value, other.Value), out var e) && (e.Tags & tag) != 0;

    /// <summary>
    /// Applies a modifier (16 §4.3–4.5) the holder perceived about the actor: personality scaling (§4.4), public
    /// multiplier, exact merge by stacking rule, floors, event fear. <paramref name="multiplier"/> carries variable
    /// values (importance, stakes, severity…).
    /// </summary>
    public float ApplyModifier(EntityId holder, EntityId actor, string modifierId, float multiplier = 1f, bool isPublic = false)
    {
        var content = _world.Content;
        var h = content.OpinionModifierHandle(modifierId);
        if (h < 0) { throw new ArgumentException($"Unknown opinion modifier '{modifierId}'.", nameof(modifierId)); }
        var def = content.OpinionModifiers[h];
        var hp = Holder(holder);
        var v = def.Value * multiplier * (isPublic ? def.PublicMultiplier : 1f);
        if (v < 0f) { v *= 0.8f + (0.4f * hp.Volatility / 100f); }
        if (v > 0f) { v *= (0.8f + (0.4f * hp.Warmth / 100f)) * (hp.Paranoid ? 0.8f : 1f); }
        v *= def.Scaling switch
        {
            OpinionScaling.Honor => 0.5f + (hp.Values.Honor / 100f),
            OpinionScaling.FamilyFaith => 0.5f + (Math.Max(hp.Values.Family, hp.Values.Faith) / 100f),
            _ => 1f,
        };

        var e = GetOrCreate(holder, actor);
        var now = _world.Clock.GameMinute;
        var index = e.Mods.FindIndex(s => s.Modifier == h);
        var slot = index >= 0 ? e.Mods[index] : new ModSlot { Modifier = (ushort)h, TimeMin = now };
        if (index >= 0 && def.Stacking == OpinionStacking.Once) { return 0f; }
        var vd = index >= 0 ? Decayed(slot, hp, now, out _) : 0f;
        var cap = def.Cap ?? float.MaxValue * MathF.Sign(v == 0f ? 1f : v);
        var newValue = def.Stacking switch
        {
            OpinionStacking.Add => cap > 0 ? MathF.Min(vd + v, cap) : MathF.Max(vd + v, cap),
            OpinionStacking.Saturate => vd + (v * (1f - (vd / cap))),
            OpinionStacking.Refresh => MathF.Sign(v) * MathF.Max(MathF.Abs(vd), MathF.Abs(v)),
            _ => v,
        };

        var floorFraction = def.FloorFraction * (hp.Vengeful && v < 0f && MathF.Abs(v) >= 25f ? 2f : 1f);
        slot.Floor += floorFraction * v;
        slot.Value = newValue;
        slot.TimeMin = now;
        slot.Count = (byte)Math.Min(255, slot.Count + 1);
        if (index >= 0) { e.Mods[index] = slot; } else { e.Mods.Add(slot); }

        if (def.Fear > 0f)
        {
            var mult = (hp.Paranoid ? 2f : 1f) * (hp.Brave ? 0.5f : 1f);
            e.FearEvent = Math.Min(100f, Fear(holder, actor) + (def.Fear * mult));
            e.FearTimeMin = now;
        }

        return v;
    }

    /// <summary>Scales a modifier's remaining value (apology accepted keeps 50 %, 16 §4.14).</summary>
    public void ScaleModifier(EntityId holder, EntityId actor, string modifierId, float keep)
    {
        if (!_edges.TryGetValue((holder.Value, actor.Value), out var e)) { return; }
        var h = _world.Content.OpinionModifierHandle(modifierId);
        var index = e.Mods.FindIndex(s => s.Modifier == h);
        if (index < 0) { return; }
        var now = _world.Clock.GameMinute;
        var slot = e.Mods[index];
        var vd = Decayed(slot, Holder(holder), now, out _);
        slot.Value = slot.Floor + ((vd - slot.Floor) * keep);
        slot.TimeMin = now;
        e.Mods[index] = slot;
    }

    /// <summary>16 §4.8 evidence: positive g → T += g·(100 − T)/100; negative l → T −= l·(0.5 + T/100); ceiling 50 + 0.5·Honesty.</summary>
    public void TrustEvidence(EntityId holder, EntityId other, float amount, float honestyReputation = 0f)
    {
        var e = GetOrCreate(holder, other);
        var hp = Holder(holder);
        if (amount >= 0f)
        {
            var g = amount * (hp.Paranoid ? 0.7f : 1f) * (hp.Gullible ? 1.5f : 1f);
            e.Trust += g * (100f - e.Trust) / 100f;
        }
        else
        {
            e.Trust -= -amount * (0.5f + (e.Trust / 100f));
        }

        e.Trust = Math.Clamp(e.Trust, 0f, Math.Min(100f, 50f + (0.5f * honestyReputation)));
    }

    /// <summary>16 §4.9: ΔF = w·(1 − F/100) in both directions; social contact capped at +6 per pair per day.</summary>
    public void Contact(EntityId a, EntityId b, float w, bool social)
    {
        var day = _world.Clock.GameMinute / 1440;
        foreach (var (x, y) in new[] { (a, b), (b, a) })
        {
            var e = GetOrCreate(x, y);
            var gain = w;
            if (social)
            {
                if (e.SocialContactDay != day) { (e.SocialContactDay, e.SocialContactToday) = (day, 0f); }
                gain = MathF.Min(w, 6f - e.SocialContactToday);
                if (gain <= 0f) { continue; }
                e.SocialContactToday += gain;
            }

            e.Familiarity += gain * (1f - (e.Familiarity / 100f));
            e.PeakFamiliarity = MathF.Max(e.PeakFamiliarity, e.Familiarity);
            e.LastContactMin = _world.Clock.GameMinute;
        }
    }

    /// <summary>16 §4.9 co-working: once the pair has spent 2 game hours on the same task at the same site today, w = 1.5.</summary>
    public void CoWorkHour(EntityId a, EntityId b)
    {
        var day = _world.Clock.GameMinute / 1440;
        var e = GetOrCreate(a, b);
        if (e.WorkDay != day) { (e.WorkDay, e.WorkHoursToday) = (day, 0f); }
        e.WorkHoursToday += 1f;
        if (e.WorkHoursToday == 2f) { Contact(a, b, 1.5f, social: false); }
    }

    /// <summary>Landfall: every pair of founders are shipmates — Familiarity U(15, 35), near-symmetric (16 §4.9).</summary>
    public void SeedShipmates()
    {
        var ids = _world.People.Ids.ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            for (var j = i + 1; j < ids.Length; j++)
            {
                var rng = new Rng(SplitMix64.Mix(_world.WorldSeed, (ulong)RngStream.Social, ids[i].Value, ids[j].Value, Salt.Shipmates));
                var f = rng.Uniform(15f, 35f);
                foreach (var (x, y) in new[] { (ids[i], ids[j]), (ids[j], ids[i]) })
                {
                    var e = GetOrCreate(x, y);
                    (e.Familiarity, e.PeakFamiliarity) = (f, f);
                }
            }
        }

        ShipmatesSeeded = true;
    }

    /// <summary>Once per game day: familiarity decay after 4 idle days, trust drift, slot cleanup, tags (16 §4.8–4.12).</summary>
    public void DailyUpdate()
    {
        var now = _world.Clock.GameMinute;
        foreach (var ((holder, other), e) in _edges)
        {
            var h = new EntityId(holder);
            var o = new EntityId(other);
            if (now - e.LastContactMin > 4 * 1440)
            {
                var floor = 0.3f * e.PeakFamiliarity;
                e.Familiarity = floor + ((e.Familiarity - floor) * Half(1440, 64f * MinutesPerDay));
            }

            e.Trust = e.Trust0 + ((e.Trust - e.Trust0) * MathF.Pow(0.9f, 1f / 32f));
            var hp = Holder(h);
            e.Mods.RemoveAll(slot => { Decayed(slot, hp, now, out var negligible); return negligible && slot.Floor == 0f; });
            UpdateTags(e, Opinion(h, o), now);
        }
    }

    private static void UpdateTags(RelationshipEdge e, float op, long now)
    {
        // Acquaintance: F ≥ 10, exit F < 5.
        if (e.Familiarity >= 10f) { e.Tags |= RelTags.Acquaintance; } else if (e.Familiarity < 5f) { e.Tags &= ~RelTags.Acquaintance; }

        // Friend: Op ≥ 30 ∧ F ≥ 30 ∧ T ≥ 40 for 2 days; exit Op < 15 ∨ T < 25.
        var friendCond = op >= 30f && e.Familiarity >= 30f && e.Trust >= 40f;
        if ((e.Tags & RelTags.Friend) == 0)
        {
            if (!friendCond) { e.FriendSinceMin = -1; }
            else if (e.FriendSinceMin < 0) { e.FriendSinceMin = now; }
            else if (now - e.FriendSinceMin >= 2 * 1440) { e.Tags |= RelTags.Friend; }
        }
        else if (op < 15f || e.Trust < 25f)
        {
            e.Tags &= ~(RelTags.Friend | RelTags.CloseFriend);
            e.FriendSinceMin = -1;
        }

        // Close friend: Op ≥ 60 ∧ F ≥ 60 ∧ T ≥ 60; exit Op < 40 ∨ T < 45.
        if (op >= 60f && e.Familiarity >= 60f && e.Trust >= 60f) { e.Tags |= RelTags.CloseFriend; }
        else if (op < 40f || e.Trust < 45f) { e.Tags &= ~RelTags.CloseFriend; }

        // Enemy: Op ≤ −50 ∧ a grave harm (proxy for 16's memory rule until M1-06: a slot ≤ −25 or a lasting floor); exit Op > −25 for 8 days.
        var grave = e.Mods.Any(s => s.Value <= -25f || s.Floor <= -5f);
        if ((e.Tags & RelTags.Enemy) == 0)
        {
            if (op <= -50f && grave) { e.Tags |= RelTags.Enemy; e.EnemyCalmSinceMin = -1; }
        }
        else if (op > -25f)
        {
            if (e.EnemyCalmSinceMin < 0) { e.EnemyCalmSinceMin = now; }
            else if (now - e.EnemyCalmSinceMin >= 8 * 1440) { e.Tags &= ~RelTags.Enemy; }
        }
        else
        {
            e.EnemyCalmSinceMin = -1;
        }

        // Rival needs a competition source (16 D6/D7, offices, contests) — arrives with those systems.
    }

    /// <summary>16 §4.6 derived terms available in M1: D3 homeland, D4 faith, D5 values alignment.</summary>
    public float Derived(EntityId holder, EntityId other)
    {
        var people = _world.People;
        int a = people.IndexOf(holder), b = people.IndexOf(other);
        if (a < 0 || b < 0) { return 0f; }
        ref readonly var pa = ref people.Personality[a];
        ref readonly var pb = ref people.Personality[b];
        var d3 = pa.Culture == pb.Culture ? 4f : -(2f + (10f * pa.Values.Tradition / 100f));   // tension 1.0 until 13's grievances
        var ca = Creed(pa.Culture);
        var cb = Creed(pb.Culture);
        var d4 = ca == cb ? 4f * pa.Values.Faith / 50f
            : (ca, cb) is ("ember_orthodox", "ember_lax") or ("ember_lax", "ember_orthodox") ? -3f
            : -(5f + (15f * pa.Values.Faith / 100f));
        return d3 + d4 + ValuesAlignment(a, b, holder, other);
    }

    /// <summary>D5: 10·cos(v_A − 50, perceived v_B − 50); A perceives B's values with noise σ = 30·(1 − F/100), stable per pair.</summary>
    private float ValuesAlignment(int a, int b, EntityId holder, EntityId other)
    {
        var people = _world.People;
        var f = Familiarity(holder, other);
        var sigma = 30f * (1f - (f / 100f));
        var rng = new Rng(SplitMix64.Mix(_world.WorldSeed, (ulong)RngStream.Social, holder.Value, other.Value, Salt.Perception));
        Span<float> va = stackalloc float[9];
        Span<float> vb = stackalloc float[9];
        Values(people.Personality[a].Values, va);
        Values(people.Personality[b].Values, vb);
        float dot = 0, na = 0, nb = 0;
        for (var k = 0; k < 9; k++)
        {
            var x = va[k] - 50f;
            var y = Math.Clamp(vb[k] + (sigma * rng.NextNormal()), 0f, 100f) - 50f;
            dot += x * y;
            na += x * x;
            nb += y * y;
        }

        return na <= 0f || nb <= 0f ? 0f : 10f * dot / MathF.Sqrt(na * nb);
    }

    private static void Values(in ValueBlock v, Span<float> into)
    {
        into[0] = v.Family; into[1] = v.Wealth; into[2] = v.Status; into[3] = v.Honor; into[4] = v.Tradition;
        into[5] = v.Faith; into[6] = v.Fairness; into[7] = v.Freedom; into[8] = v.Loyalty;
    }

    private string Creed(ushort culture)
    {
        if (culture >= _world.Content.Cultures.Count) { return "none"; }
        return _world.Content.Cultures[culture].Id switch
        {
            "culture.varrow" => "ember_orthodox",
            "culture.osmeri" => "ember_lax",
            "culture.ashen_reform" => "ashen_reform",
            "culture.brannoch" => "old_ways",
            var other => other,
        };
    }

    /// <summary>16 §4.8 baseline: clamp(35 + 0.3·(W − 50) − 15·Paranoid + 10·sameHomeland + 25·kin, 5, 80).</summary>
    private float TrustBaseline(EntityId holder, EntityId other)
    {
        var people = _world.People;
        int a = people.IndexOf(holder), b = people.IndexOf(other);
        if (a < 0) { return 35f; }
        var hp = Holder(holder);
        var same = b >= 0 && people.Personality[a].Culture == people.Personality[b].Culture ? 10f : 0f;
        return Math.Clamp(35f + (0.3f * (hp.Warmth - 50f)) - (hp.Paranoid ? 15f : 0f) + same, 5f, 80f);
    }

    private float Decayed(in ModSlot slot, in HolderTraits hp, long now, out bool negligible)
    {
        var def = _world.Content.OpinionModifiers[slot.Modifier];
        var kappa = 1f;
        if (slot.Value < 0f)
        {
            if (hp.Warmth >= 70) { kappa *= 0.75f; }   // forgiving
            if (hp.Vengeful) { kappa *= 2f; }
        }

        var above = (slot.Value - slot.Floor) * Half(now - slot.TimeMin, def.HalfLifeDays * MinutesPerDay * kappa);
        negligible = MathF.Abs(above) < 1f;
        return slot.Floor + (negligible ? 0f : above);
    }

    private static float Half(long minutes, float halfLifeMinutes) => SimMath.Exp(-0.6931472f * minutes / halfLifeMinutes);

    private readonly record struct HolderTraits(byte Warmth, byte Volatility, ValueBlock Values, bool Paranoid, bool Vengeful, bool Brave, bool Gullible);

    private HolderTraits Holder(EntityId id)
    {
        var people = _world.People;
        var row = people.IndexOf(id);
        if (row < 0) { return new HolderTraits(50, 50, default, false, false, false, false); }
        var p = people.Personality[row];
        var c = _world.Content;
        return new HolderTraits(p.Warmth, p.Volatility, p.Values, HasTrait(c, p.Traits, "trait.paranoid"), HasTrait(c, p.Traits, "trait.vengeful"),
            HasTrait(c, p.Traits, "trait.brave"), HasTrait(c, p.Traits, "trait.gullible"));
    }

    private static bool HasTrait(ContentDatabase content, ulong traits, string id)
    {
        var h = content.TraitHandle(id);
        return h >= 0 && (traits & (1UL << h)) != 0;
    }

    internal void HashInto(XxHash64 h)
    {
        L(h, ShipmatesSeeded ? 1 : 0);
        foreach (var ((holder, other), e) in _edges)
        {
            L(h, (long)holder); L(h, (long)other);
            F(h, e.Trust); F(h, e.Trust0); F(h, e.Familiarity); F(h, e.PeakFamiliarity); F(h, e.FearEvent);
            L(h, e.FearTimeMin); L(h, e.LastContactMin); L(h, e.FriendSinceMin); L(h, e.EnemyCalmSinceMin); L(h, (long)e.Tags);
            F(h, e.SocialContactToday); L(h, e.SocialContactDay); F(h, e.WorkHoursToday); L(h, e.WorkDay);
            foreach (var s in e.Mods) { L(h, s.Modifier); F(h, s.Value); F(h, s.Floor); L(h, s.TimeMin); L(h, s.Count); }
        }
    }

    private static void L(XxHash64 h, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, v);
        h.Append(b);
    }

    private static void F(XxHash64 h, float v)
    {
        Span<byte> b = stackalloc byte[4];
        BitConverter.TryWriteBytes(b, v);
        h.Append(b);
    }

    /// <summary>Flat records for saves (blittable).</summary>
    public struct EdgeRecord
    {
        public ulong Holder, Other;
        public float Trust, Trust0, Familiarity, PeakFamiliarity, FearEvent, SocialContactToday, WorkHoursToday;
        public long FearTimeMin, LastContactMin, FriendSinceMin, EnemyCalmSinceMin, SocialContactDay, WorkDay;
        public ushort Tags;
        public int ModStart, ModCount;
    }

    internal (EdgeRecord[] Edges, ModSlot[] Mods) Export()
    {
        var edges = new List<EdgeRecord>(_edges.Count);
        var mods = new List<ModSlot>();
        foreach (var ((holder, other), e) in _edges)
        {
            edges.Add(new EdgeRecord
            {
                Holder = holder, Other = other, Trust = e.Trust, Trust0 = e.Trust0, Familiarity = e.Familiarity, PeakFamiliarity = e.PeakFamiliarity,
                FearEvent = e.FearEvent, SocialContactToday = e.SocialContactToday, FearTimeMin = e.FearTimeMin, LastContactMin = e.LastContactMin,
                FriendSinceMin = e.FriendSinceMin, EnemyCalmSinceMin = e.EnemyCalmSinceMin, SocialContactDay = e.SocialContactDay,
                WorkHoursToday = e.WorkHoursToday, WorkDay = e.WorkDay,
                Tags = (ushort)e.Tags, ModStart = mods.Count, ModCount = e.Mods.Count,
            });
            mods.AddRange(e.Mods);
        }

        return ([.. edges], [.. mods]);
    }

    internal void Import(EdgeRecord[] edges, ModSlot[] mods, bool shipmatesSeeded)
    {
        _edges.Clear();
        foreach (var r in edges)
        {
            var e = new RelationshipEdge
            {
                Trust = r.Trust, Trust0 = r.Trust0, Familiarity = r.Familiarity, PeakFamiliarity = r.PeakFamiliarity, FearEvent = r.FearEvent,
                SocialContactToday = r.SocialContactToday, FearTimeMin = r.FearTimeMin, LastContactMin = r.LastContactMin,
                FriendSinceMin = r.FriendSinceMin, EnemyCalmSinceMin = r.EnemyCalmSinceMin, SocialContactDay = r.SocialContactDay, Tags = (RelTags)r.Tags,
                WorkHoursToday = r.WorkHoursToday, WorkDay = r.WorkDay,
            };
            for (var k = 0; k < r.ModCount; k++) { e.Mods.Add(mods[r.ModStart + k]); }
            _edges.Add((r.Holder, r.Other), e);
        }

        ShipmatesSeeded = shipmatesSeeded;
    }
}
