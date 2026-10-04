using System.IO.Hashing;
using System.Runtime.CompilerServices;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Social;

public enum RepAxis : byte { Honesty, Lawfulness, Peaceableness, Courage, Generosity, Piety, Competence }

/// <summary>Seven reputation axes (16 §8.1), −100…+100.</summary>
[InlineArray(7)]
public struct RepAxes
{
    private float _e0;
}

/// <summary>
/// Reputation (16 §8): personal impressions <c>R_A,x(B)</c> from A's beliefs about B, the third-party view <c>R³ᵖ</c>
/// (no self-involved beliefs, §4.7), D9 <c>reputation_impression</c>, and community Renown (§8.2) recomputed nightly.
/// Impressions age to the start of the current day, so a value is a pure function of (beliefs, day) and is cached until
/// either changes. Renown is state (saved, hashed); the impression cache is not.
/// </summary>
public sealed class ReputationStore
{
    /// <summary>Per-axis half-lives in days (16 §8.1).</summary>
    public static ReadOnlySpan<float> HalfLifeDays => [64f, 64f, 48f, 32f, 32f, 32f, 48f];

    private readonly SimWorld _world;
    private readonly SortedDictionary<ulong, float> _renown = [];
    private readonly Dictionary<(ulong, ulong), (long Version, long Day, RepAxes Full, RepAxes Third)> _cache = [];
    private ContentDatabase? _impactsFor;
    private float[][] _impacts = [];

    public ReputationStore(SimWorld world) => _world = world;

    /// <summary>The axis for a content axis key ("honesty" …).</summary>
    public static RepAxis AxisOf(string key) => Enum.Parse<RepAxis>(key, ignoreCase: true);

    /// <summary>Renown of a person in the (single, M1) community, 0–100; 0 before the first nightly pass.</summary>
    public float Renown(EntityId person) => _renown.TryGetValue(person.Value, out var r) ? r : 0f;

    public bool HasRenown => _renown.Count > 0;

    /// <summary>A's impression of B on one axis: <c>R_full</c> (all beliefs) or <c>R³ᵖ</c> (thirdParty: no self-involved ones).</summary>
    public float R(EntityId holder, EntityId subject, RepAxis axis, bool thirdParty = false)
    {
        var (full, third) = Impressions(holder, subject);
        return thirdParty ? third[(int)axis] : full[(int)axis];
    }

    /// <summary>
    /// D9 (16 §4.6): <c>Σ_x R³ᵖ_x · w_x(A) / 100</c>, cap ±25. w from A's values: Honesty←Fairness, Generosity←(Fairness +
    /// Family)/2, Courage←Honor, Peaceableness←Warmth, Piety←Faith. Lawfulness carries no effects in M1 (30 §5); Competence none.
    /// </summary>
    public float D9(int holderRow, EntityId holder, EntityId subject)
    {
        var (_, r) = Impressions(holder, subject);
        ref readonly var p = ref _world.People.Personality[holderRow];
        var v = p.Values;
        var sum = (r[(int)RepAxis.Honesty] * v.Fairness) + (r[(int)RepAxis.Generosity] * (v.Fairness + v.Family) / 2f)
                + (r[(int)RepAxis.Courage] * v.Honor) + (r[(int)RepAxis.Peaceableness] * p.Warmth) + (r[(int)RepAxis.Piety] * v.Faith);
        return Math.Clamp(sum / 100f, -25f, 25f);
    }

    private (RepAxes Full, RepAxes Third) Impressions(EntityId holder, EntityId subject)
    {
        var day = _world.Clock.GameMinute / 1440;
        var version = _world.Beliefs.Version;
        if (_cache.TryGetValue((holder.Value, subject.Value), out var hit) && hit.Version == version && hit.Day == day) { return (hit.Full, hit.Third); }

        var impacts = Impacts();
        var claims = _world.Claims;
        var beliefs = _world.Beliefs.Span(holder);
        RepAxes full = default, third = default;
        var dayStart = day * 1440;
        for (var k = 0; k < beliefs.Length; k++)
        {
            var b = beliefs[k];
            if (b.C < BeliefStore.Hold) { continue; }
            ref readonly var c = ref claims[b.Claim];
            if (c.Subject != subject.Value || !Strongest(beliefs, k, claims)) { continue; }
            var imp = impacts[c.Predicate];
            if (imp.Length == 0) { continue; }
            var selfInvolved = c.Object == holder.Value;
            var ageDays = MathF.Max(0f, (dayStart - (c.TimeMin >= 0 ? c.TimeMin : b.HeardMin)) / 1440f);
            var weight = b.C * MathF.Max(1f, c.Magnitude) * (b.FirstHand ? 1.5f : 1f);
            for (var x = 0; x < 7; x++)
            {
                if (imp[x] == 0f) { continue; }
                var term = weight * imp[x] * SimMath.Pow(0.5f, ageDays / HalfLifeDays[x]);
                full[x] += term;
                if (!selfInvolved) { third[x] += term; }
            }
        }

        for (var x = 0; x < 7; x++)
        {
            full[x] = 100f * MathF.Tanh(full[x] / 100f);
            third[x] = 100f * MathF.Tanh(third[x] / 100f);
        }

        _cache[(holder.Value, subject.Value)] = (version, day, full, third);
        return (full, third);
    }

    /// <summary>Only the strongest variant of each original claim counts (16 §7.6); ties go to the earlier belief.</summary>
    private static bool Strongest(ReadOnlySpan<Belief> beliefs, int k, ClaimStore claims)
    {
        var root = claims.Root(beliefs[k].Claim);
        var subject = claims[beliefs[k].Claim].Subject;
        for (var j = 0; j < beliefs.Length; j++)
        {
            if (j == k || beliefs[j].C < BeliefStore.Hold) { continue; }
            var o = beliefs[j];
            if (claims[o.Claim].Subject != subject || claims.Root(o.Claim) != root) { continue; }
            if (o.C > beliefs[k].C || (o.C == beliefs[k].C && j < k)) { return false; }
        }

        return true;
    }

    /// <summary>The axis a predicate weighs most on (largest |impact|; ties to the earlier axis), or −1 if it has none.</summary>
    public int MainAxis(int predicate)
    {
        var imp = Impacts()[predicate];
        var best = -1;
        for (var x = 0; x < imp.Length; x++) { if (imp[x] != 0f && (best < 0 || MathF.Abs(imp[x]) > MathF.Abs(imp[best]))) { best = x; } }
        return best;
    }

    private float[][] Impacts()
    {
        var content = _world.Content;
        if (ReferenceEquals(_impactsFor, content)) { return _impacts; }
        _impacts = [.. content.ClaimPredicates.Select(d =>
        {
            if (d.Axes is null || d.Axes.Count == 0) { return Array.Empty<float>(); }
            var a = new float[7];
            foreach (var (axis, v) in d.Axes) { a[(int)AxisOf(axis)] = v; }
            return a;
        })];
        _impactsFor = content;
        return _impacts;
    }

    /// <summary>
    /// Nightly 16 §8.2 Renown: <c>100 · Σ_m w_m · knows(m, B) / Σ_m w_m</c>, <c>w_m = 1 + Renown(m)/50</c> (opinion leaders
    /// count up to 3×; the weights come from the previous pass, or 1 on the first), <c>knows = min(1, F/30 + 0.5·[holds a
    /// belief about B at c ≥ 0.5])</c>. One community: the whole world (M1 camp).
    /// </summary>
    public void Recompute()
    {
        var people = _world.People;
        var n = people.Count;
        if (n == 0) { return; }
        Span<float> weights = n <= 256 ? stackalloc float[n] : new float[n];
        Span<float> knows = n <= 64 ? stackalloc float[n * n] : new float[n * n];   // knows[m·n + b]
        for (var m = 0; m < n; m++)
        {
            weights[m] = 1f + (Renown(people.Ids[m]) / 50f);
            for (var b = 0; b < n; b++)
            {
                if (m == b) { continue; }
                var k = _world.Relationships.Familiarity(people.Ids[m], people.Ids[b]) / 30f;
                if (k < 1f && HoldsAbout(people.Ids[m], people.Ids[b])) { k += 0.5f; }
                knows[(m * n) + b] = MathF.Min(1f, k);
            }
        }

        Span<float> next = n <= 256 ? stackalloc float[n] : new float[n];
        for (var pass = 0; pass < (HasRenown ? 1 : 2); pass++)
        {
            for (var b = 0; b < n; b++)
            {
                float num = 0f, den = 0f;
                for (var m = 0; m < n; m++)
                {
                    if (m == b) { continue; }
                    num += weights[m] * knows[(m * n) + b];
                    den += weights[m];
                }

                next[b] = den == 0f ? 0f : 100f * num / den;
            }

            for (var b = 0; b < n; b++)
            {
                _renown[people.Ids[b].Value] = next[b];
                weights[b] = 1f + (next[b] / 50f);
            }
        }
    }

    private bool HoldsAbout(EntityId holder, EntityId subject)
    {
        foreach (var b in _world.Beliefs.Span(holder)) { if (b.C >= BeliefStore.Hold && _world.Claims[b.Claim].Subject == subject.Value) { return true; } }
        return false;
    }

    /// <summary>16 §8.2 community reputation on one axis (for institutions and the player's "what the village thinks").</summary>
    public float Community(EntityId subject, RepAxis axis)
    {
        var people = _world.People;
        float num = 0f, den = 0f;
        for (var m = 0; m < people.Count; m++)
        {
            var id = people.Ids[m];
            if (id == subject) { continue; }
            var knows = MathF.Min(1f, (_world.Relationships.Familiarity(id, subject) / 30f) + (HoldsAbout(id, subject) ? 0.5f : 0f));
            if (knows <= 0f) { continue; }
            var w = 1f + (Renown(id) / 50f);
            num += w * R(id, subject, axis);
            den += w;
        }

        return den == 0f ? 0f : num / den;
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[12];
        foreach (var (id, r) in _renown)
        {
            BitConverter.TryWriteBytes(b, id);
            BitConverter.TryWriteBytes(b[8..], r);
            h.Append(b);
        }
    }

    public struct Row
    {
        public ulong Person;
        public float Renown;
    }

    internal Row[] Export() => [.. _renown.Select(kv => new Row { Person = kv.Key, Renown = kv.Value })];

    internal void Import(Row[] rows)
    {
        _renown.Clear();
        _cache.Clear();
        foreach (var r in rows) { _renown[r.Person] = r.Renown; }
    }
}
