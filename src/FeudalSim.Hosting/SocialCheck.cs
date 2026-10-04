using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Time;
using FeudalSim.Sim.World;

namespace FeudalSim.Hosting;

/// <summary>One world's 30-day social run (30 M1 exit: no deadlocks, ≥ 1 emergent dispute per 10 days).</summary>
public sealed record SocialRun(ulong Seed, int Days, int Disputes, int Fights, int Interventions, int Insults, int StuckPeople, double LongestSameHours, double LongestTravelHours,
    int ActivitiesStarted, ulong FinalHash, double WallSeconds)
{
    /// <summary>Disputes by the highest rung they reached (index = rung 0–7).</summary>
    public int[] TopRungs { get; init; } = new int[8];

    public const double StuckSameHours = 16, StuckTravelHours = 2;

    public bool NoDeadlock => StuckPeople == 0 && ActivitiesStarted > 0;

    public double DisputesPer10Days => Days == 0 ? 0 : Disputes * 10.0 / Days;
}

/// <summary>
/// M1-22's headless social sim: the camp for N days with no player, every step. An <b>emergent dispute</b> is an unscripted
/// confrontation between two settlers (16 §9: a <see cref="ConfrontationEscalated"/> on a pair not already quarrelling
/// that game day). A <b>deadlock</b> is anyone awake on the same activity for more than 16 game hours, or still walking to
/// a task after 2 — or a camp that stops starting activities.
/// </summary>
public static class SocialCheck
{
    /// <summary>The §9 rung an escalation response stands for.</summary>
    public static byte Rung(string option) => option switch { "retort" => 2, "threaten" => 3, "shove" => 4, "attack_brawl" => 5, "laugh_off" => 1, _ => 0 };

    public static SocialRun Run(ScenarioDef scenario, ContentDatabase content, int days)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var w = (scenario with { Days = days }).CreateWorld(content, SerialJobScheduler.Instance);
        var stepsPerDay = SimClock.MsPerGameDay / w.Clock.GameMsPerStep;
        int disputes = 0, fights = 0, interventions = 0, insults = 0, started = 0;
        var pairsToday = new Dictionary<(ulong, ulong), byte>();
        var top = new int[8];
        var lastDay = -1L;
        var since = new Dictionary<ulong, (short Action, byte Phase, long StartMs)>();
        double longestSame = 0, longestTravel = 0;
        var stuck = new HashSet<ulong>();
        for (long s = 0; s < days * stepsPerDay; s++)
        {
            var o = w.Step();
            var day = w.Clock.GameMinute / 1440;
            if (day != lastDay) { lastDay = day; foreach (var r in pairsToday.Values) { top[Math.Min(r, (byte)7)]++; } pairsToday.Clear(); }
            foreach (var e in o.Events)
            {
                switch (e.Payload)
                {
                    // A dispute: a settler answers a provocation from another on the §9 ladder at Argument or above
                    // (retort, threat, shove, brawl) — one per pair per game day, kept at the highest rung it reached.
                    case DecisionResolved { Owner: Sim.Social.EscalationOwner.Id } d when Rung(d.Chosen) >= 2 && d.Chooser != w.PlayerId && !d.Chooser.IsNone:
                        var other = w.People.IndexOf(d.Chooser) is var cr and >= 0 ? w.People.Emotions[cr].AngerTarget : Sim.Core.EntityId.None;   // Provoke sets it
                        if (other.IsNone || other == w.PlayerId) { break; }
                        var pair = d.Chooser.Value < other.Value ? (d.Chooser.Value, other.Value) : (other.Value, d.Chooser.Value);
                        if (!pairsToday.TryGetValue(pair, out var was)) { disputes++; }
                        pairsToday[pair] = Math.Max(was, Rung(d.Chosen));
                        break;
                    case FightResolved: fights++; break;
                    case BystanderIntervened: interventions++; break;
                    case InteractionResolved { Kind: var k } when k.Contains("insult", StringComparison.Ordinal): insults++; break;
                }
            }

            if (s % 60 != 0) { continue; }   // every 6 game minutes
            var p = w.People;
            for (var i = 0; i < p.Count; i++)
            {
                if (i == w.PlayerRow) { continue; }
                ref readonly var a = ref p.Activity[i];
                var id = p.Ids[i].Value;
                var key = (a.Action, a.Phase, a.StartedGameMs);
                if (!since.TryGetValue(id, out var prev) || prev != key) { since[id] = key; started++; continue; }
                if (a.Has(ActivityState.Asleep)) { continue; }
                var hours = (w.Clock.GameMs - a.StartedGameMs) / (double)SimClock.MsPerGameHour;
                if (a.Phase == 0) { longestTravel = Math.Max(longestTravel, hours); if (hours > SocialRun.StuckTravelHours) { stuck.Add(id); } }
                else { longestSame = Math.Max(longestSame, hours); if (hours > SocialRun.StuckSameHours) { stuck.Add(id); } }
            }
        }

        foreach (var r in pairsToday.Values) { top[Math.Min(r, (byte)7)]++; }
        return new SocialRun(scenario.Seed, days, disputes, fights, interventions, insults, stuck.Count, longestSame, longestTravel, started, StateHasher.Hash(w), clock.Elapsed.TotalSeconds) { TopRungs = top };
    }
}
