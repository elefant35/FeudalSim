using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// LOD3 statistical update (21 §15.6) for the camp, once per game day per person (or per macro step during a skip), for a
/// fraction <c>f</c> of a day. A first, uncalibrated version used by spike S6 to measure cost:
/// <list type="number">
/// <item>Production: the person works the schedule's work hours on one purposeful camp action, drawn per day by stock
/// pressure (the hourly scorer's C term); yield × skill (0.6 + 0.8·skill/100) × a mood factor clamp(1 + mood/500, 0.8, 1.2).
/// Inputs (negative stock rates) are capped by what the stocks hold.</item>
/// <item>Consumption and physical needs: the day's Satiety demand (11 §2.1 rates over the schedule's sleep, work and rest
/// hours) is eaten from the stores; fed people sit at the daily equilibrium (Satiety 80, Hydration 85, Energy 85 × bedding
/// / 0.85), shortfall comes off Satiety.</item>
/// <item>Psychological needs and mood: Social and Purpose by daily mass balance (gains over social/work hours, decay over
/// the other waking hours); Comfort, Safety, Status and emotions decay exactly over 24·f hours; mood is composed (21 §6.4)
/// and its average set to it.</item>
/// <item>Relationships: I·f friendly contacts with crewmates (I = 2 + Sociability/16), each a chat (both directions, 75 %
/// success) without memories — LOD3 records events only.</item>
/// </list>
/// The calibration tables and the LOD1-vs-LOD3 fidelity check of 21 §15.6 (production ±10 %, mood ±8 …) are M4 work (Interludes).
/// </summary>
public sealed class Lod3System : ISimSystem
{
    private ContentDatabase? _cachedFor;
    private ushort _cachedSchedule = 0xFFFF;
    private float _sleepH, _workH, _socialH, _awakeH;
    private int[] _work = [];
    private int[] _workSkill = [];
    private float[] _moodBaseline = [];
    private float _satSleep, _satRest;
    private float[] _satByLevel = new float[5];
    private int _chatted = -1;

    public string Name => "Lod3";
    public SimPhase Phase => SimPhase.Resolve;

    /// <summary>Daily equilibria for fed, rested people (21 §15.6 step 3; uncalibrated, S6).</summary>
    public const float FedSatiety = 80f, Hydrated = 85f, RestedEnergy = 85f;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (world.Due.Lod3Due == 0 || world.Camp.Active == 0) { return; }
        Cache(world);
        var people = world.People;
        var due = world.Due;
        var day = (ulong)(ctx.GameMs / Time.SimClock.MsPerGameDay);
        for (var k = 0; k < due.Count; k++)
        {
            var i = due.Rows[k];
            if (people.Lod[i].Tier != LodTier.Lod3 || due.Dt(k) <= 0) { continue; }
            var f = due.Dt(k) / (float)Time.SimClock.MsPerGameDay;
            var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Lod, people.Ids[i].Value, day, Salt.Lod3));
            var level = Work(world, i, f, ref rng);
            Needs(world, i, f, level);
            Social(world, i, f, ref rng);
        }
    }

    private ActivityLevel Work(SimWorld world, int i, float f, ref Rng rng)
    {
        if (_work.Length == 0 || _workH <= 0f) { return ActivityLevel.Rest; }
        var people = world.People;
        var actions = world.Content.Actions;

        // One work action for the day, by stock pressure with Gumbel noise (keyed per person and day).
        var choice = -1;
        var bestKey = float.NegativeInfinity;
        for (var w = 0; w < _work.Length; w++)
        {
            var def = actions[_work[w]];
            if (def.Requires is { } req && !Meets(world, req)) { continue; }
            var c = def.StockPressure is { } sp ? Math.Clamp(1f - (world.Camp.Stock(sp.Stock) / sp.Comfortable), 0.20f, 1f) : 1f;
            var key = SimMath.Log(c) - SimMath.Log(-SimMath.Log(MathF.Max(1e-7f, rng.NextFloat01())));
            if (key > bestKey) { (bestKey, choice) = (key, w); }
        }

        if (choice < 0) { return ActivityLevel.Rest; }
        var action = actions[_work[choice]];
        var yield = _workSkill[choice] < 0 ? 1f : 0.6f + (0.8f * people.SkillLevels(i)[_workSkill[choice]] / 100f);
        var moodFactor = Math.Clamp(1f + (people.Mood[i].Smoothed / 500f), 0.8f, 1.2f);
        var hours = _workH * f;
        if (action.StockPerHour is { } stocks)
        {
            // Inputs are capped by what is in store; the whole action scales with the shortest input.
            var scale = 1f;
            foreach (var (stock, perHour) in stocks)
            {
                if (perHour < 0f) { scale = MathF.Min(scale, world.Camp.Stock(stock) / (-perHour * hours)); }
            }

            foreach (var (stock, perHour) in stocks)
            {
                world.Camp.AddStock(stock, perHour * hours * scale * (perHour > 0f ? yield * moodFactor : 1f));
            }
        }

        ref var n = ref people.Needs[i];
        var dil = (people.Personality[i].Diligence - 50f) / 15f;
        var purposeGain = (PrimarySkill(world, i, action) ? 10f : 6f) * hours;
        var purposeLoss = 2f * (1f + (0.3f * dil)) * (_awakeH - _workH) * f;
        n.Purpose = Math.Clamp(n.Purpose + purposeGain - purposeLoss, 0f, 100f);
        return action.Activity;
    }

    private void Needs(SimWorld world, int i, float f, ActivityLevel workLevel)
    {
        var people = world.People;
        ref var n = ref people.Needs[i];
        ref var e = ref people.Emotions[i];
        ref var m = ref people.Mood[i];
        ref readonly var p = ref people.Personality[i];

        // Food: the day's Satiety demand at the schedule's activity levels, eaten from the stores.
        var restH = MathF.Max(0f, 24f - _sleepH - _workH);
        var demand = ((_sleepH * _satSleep) + (_workH * _satByLevel[(int)workLevel]) + (restH * _satRest)) * f;
        var eaten = MathF.Min(demand, world.Camp.Food);
        world.Camp.AddStock("food", -eaten);
        n.Satiety = Math.Clamp(FedSatiety - (demand - eaten), 0f, 100f);
        n.Hydration = Hydrated;
        n.Energy = Math.Clamp(RestedEnergy * world.Camp.Bedding / 0.85f, 0f, 100f);

        // Social by mass balance: talk at the fire vs decay over the other waking hours.
        var zSoc = (p.Sociability - 50f) / 15f;
        n.Social = Math.Clamp(n.Social + (((40f * _socialH) - (3f * (1f + (0.3f * zSoc)) * (_awakeH - _socialH))) * f), 0f, 100f);

        // Tracking needs and emotions over 24·f hours (the PsychologySystem rates).
        var dtH = 24f * f;
        n.Comfort += (PsychologySystem.LandfallComfortTarget - n.Comfort) * (1f - SimMath.Pow(0.85f, dtH));
        n.Safety += (100f - n.Safety) * (1f - SimMath.Pow(0.95f, dtH));
        var content = world.Content;
        var standing = p.Profession < content.Professions.Count ? content.Professions[p.Profession].Prestige : 0f;
        var statusTarget = 50f + (50f * SimMath.Tanh((standing - (20f + (0.6f * p.Values.Status))) / 25f));
        n.Status += (statusTarget - n.Status) * (1f - SimMath.Pow(0.97f, dtH));
        var zVol = (p.Volatility - 50f) / 15f;
        var volHl = 1f + (0.15f * zVol);
        e.Anger *= Decay(dtH, 4f * volHl);
        e.Fear *= Decay(dtH, 1f * volHl);
        e.Grief *= Decay(dtH, 72f);
        e.Joy *= Decay(dtH, 6f);
        e.Shame *= Decay(dtH, 24f);
        e.Jealousy *= Decay(dtH, 24f);
        e.UpdatedGameMs = world.Clock.GameMs;

        var baseline = 0f;
        for (var bits = p.Traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < _moodBaseline.Length) { baseline += _moodBaseline[h]; }
        }

        m.Value = PsychologySystem.ComposeMood(n, e, zVol, baseline, thoughts: 0f);
        m.Smoothed = m.Value;
    }

    private void Social(SimWorld world, int i, float f, ref Rng rng)
    {
        var people = world.People;
        var crew = people.Count <= FeudalSim.Sim.Social.RelationshipStore.OneShipMax ? people.Count : FeudalSim.Sim.Social.RelationshipStore.CrewSize;
        var first = i / crew * crew;
        var size = Math.Min(crew, people.Count - first);
        if (size < 2 || _chatted < 0) { return; }
        var budget = (2f + (people.Personality[i].Sociability / 16f)) * f;
        var contacts = (int)budget + (rng.Chance(budget - (int)budget) ? 1 : 0);
        var rel = world.Relationships;
        for (var c = 0; c < contacts; c++)
        {
            var j = first + rng.Range(0, size - 1);
            if (j >= i) { j++; }
            EntityId a = people.Ids[i], b = people.Ids[j];
            rel.Contact(a, b, 3f, social: true);
            rel.Contact(b, a, 3f, social: true);
            if (rng.Chance(0.75f))
            {
                rel.ApplyModifier(a, b, "opinion.chatted");
                rel.ApplyModifier(b, a, "opinion.chatted");
            }
        }
    }

    private static bool Meets(SimWorld world, IReadOnlyDictionary<string, float> requires)
    {
        foreach (var (stock, min) in requires) { if (world.Camp.Stock(stock) < min) { return false; } }
        return true;
    }

    private static bool PrimarySkill(SimWorld world, int i, ActionDef def)
    {
        var prof = world.People.Personality[i].Profession;
        return def.Skill is not null && prof < world.Content.Professions.Count && world.Content.Professions[prof].Primary.Contains(def.Skill);
    }

    private static float Decay(float dtHours, float halfLifeHours) => SimMath.Exp(-dtHours * 0.6931472f / halfLifeHours);

    private void Cache(SimWorld world)
    {
        var content = world.Content;
        if (ReferenceEquals(_cachedFor, content) && _cachedSchedule == world.Camp.Schedule) { return; }
        (_cachedFor, _cachedSchedule) = (content, world.Camp.Schedule);
        _sleepH = _workH = _socialH = 0f;
        if (world.Camp.Schedule < content.Schedules.Count)
        {
            foreach (var b in content.Schedules[world.Camp.Schedule].Blocks)
            {
                var from = Minutes(b.From);
                var to = Minutes(b.To);
                var h = (to > from ? to - from : 1440 - from + to) / 60f;
                switch (b.Block)
                {
                    case "sleep": _sleepH += h; break;
                    case "work": _workH += h; break;
                    case "social": _socialH += h; break;
                }
            }
        }

        _awakeH = 24f - _sleepH;
        var work = new List<int>();
        for (var a = 0; a < content.Actions.Count; a++) { if (content.Actions[a].Purposeful) { work.Add(a); } }
        _work = [.. work];
        _workSkill = [.. _work.Select(a => content.Actions[a].Skill is { } s ? content.SkillHandle(s) : -1)];
        _moodBaseline = [.. content.Traits.Select(t => t.Effects?.MoodBaseline ?? 0f)];
        var decay = content.Need("need.satiety")?.DecayPerHour;
        _satByLevel = decay is null
            ? [2f, 3f, NeedsDecaySystem.DefaultSatietyPerHour, 5f, 7f]
            : [decay.Sleep, decay.Rest, decay.Light, decay.Moderate, decay.Heavy];
        (_satSleep, _satRest) = (_satByLevel[(int)ActivityLevel.Sleep], _satByLevel[(int)ActivityLevel.Rest]);
        _chatted = content.OpinionModifierHandle("opinion.chatted");

        static int Minutes(string hhmm) => (int.Parse(hhmm[..2], System.Globalization.CultureInfo.InvariantCulture) * 60) + int.Parse(hhmm[3..], System.Globalization.CultureInfo.InvariantCulture);
    }
}
