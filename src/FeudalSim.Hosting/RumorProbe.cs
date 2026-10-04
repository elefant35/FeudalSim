using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.Time;

namespace FeudalSim.Hosting;

/// <summary>
/// One propagation run. <c>Heard</c> = reached by the claim or a variant (witnessed or told, whatever the listener made of it —
/// 16 §7.1's doubt band included); <c>Held</c> = holding it at c ≥ 0.5. Shares are of everyone but the subject.
/// </summary>
public sealed record RumorRun(string Predicate, float Juiciness, int Witnesses, double? T50Days, double? T90Days, double HeardDay3, double HeldDay3,
    double FinalHeard, double FinalHeld, double VariantShareAtT50, int Exchanges, int Taken);

/// <summary>
/// 16 §7.8 / M1 exit probe. With <c>witnesses &gt; 0</c>: one true claim about one settler, seeded with that many first-hand
/// witnesses on day 2 at 08:00 (§7.8's toy setup). With <c>witnesses = 0</c>: a public event — the claim is witnessed through
/// the normal path (<see cref="Rumors.Witness"/>) by whoever is in range of someone socializing at the evening fire on day 2.
/// Samples hourly. The seeding is outside the input log (a measurement, not play).
/// </summary>
public static class RumorProbe
{
    public static RumorRun Run(ScenarioDef scenario, ContentDatabase content, string predicate, int days, int witnesses = 3)
    {
        var world = scenario.CreateWorld(content, SerialJobScheduler.Instance);
        while (world.People.Count == 0) { world.Step(); }   // settlers spawn on the first steps
        var n = world.People.Count;
        int root, subject;
        if (witnesses > 0)
        {
            StepTo(world, ((2 * 1440) + (8 * 60)) * SimClock.MsPerGameMinute);
            subject = (int)(scenario.Seed % (ulong)n);
            Span<int> rows = stackalloc int[Math.Min(witnesses, n - 1)];
            for (var k = 0; k < rows.Length; k++) { rows[k] = (subject + 1 + k) % n; }
            root = Rumors.SeedWitnessed(world, predicate, subject, rows);
        }
        else
        {
            (root, subject) = AtTheFire(world, predicate, (int)scenario.Seed);
        }

        var heard = new HashSet<ulong>();
        for (var k = 0; k < n; k++) { if (world.Beliefs.Get(world.People.Ids[k], root) is not null && k != subject) { heard.Add(world.People.Ids[k].Value); } }
        var witnessed = heard.Count;
        var audience = n - 1;
        double? t50 = null, t90 = null;
        double variantShare = 0, heardDay3 = 0, heldDay3 = 0, held = 0;
        int exchanges = 0, taken = 0;
        var start = world.Clock.GameMs;
        for (var hour = 1; hour <= days * 24; hour++)
        {
            while (world.Clock.GameMs < start + (hour * 60L * SimClock.MsPerGameMinute))
            {
                foreach (var e in world.Step().Events)
                {
                    if (e.Payload is not GossipExchanged g || g.Root != root) { continue; }
                    exchanges++;
                    if (g.Option != ToldOption.Doubt) { taken++; }
                    if (g.Listener.Value != world.Claims[root].Subject) { heard.Add(g.Listener.Value); }
                }
            }

            var holders = world.Beliefs.Holders(world.Claims, root, BeliefStore.Hold, out var variants);
            held = holders / (double)audience;
            var reach = heard.Count / (double)audience;
            if (t50 is null && reach >= 0.5) { (t50, variantShare) = (hour / 24.0, holders == 0 ? 0 : variants / (double)holders); }
            if (t90 is null && reach >= 0.9) { t90 = hour / 24.0; }
            if (hour == 72) { (heardDay3, heldDay3) = (reach, held); }
        }

        return new RumorRun(predicate, content.ClaimPredicates[content.ClaimHandle(predicate)].Juiciness, witnessed, t50, t90, heardDay3, heldDay3,
            heard.Count / (double)audience, held, variantShare, exchanges, taken);
    }

    /// <summary>Day 2 from 19:00: the first quarter-hour with two people socializing at the fire; one acts on the other in front of everyone in range.</summary>
    private static (int Root, int Subject) AtTheFire(SimWorld world, string predicate, int seed)
    {
        var socialize = ContentDatabase.HandleOf(world.Content.Actions, "action.socialize", a => a.Id);
        var people = world.People;
        Span<int> atFire = stackalloc int[people.Count];
        for (var minute = (2 * 1440) + (19 * 60); ; minute += 15)
        {
            StepTo(world, minute * SimClock.MsPerGameMinute);
            var count = 0;
            for (var k = 0; k < people.Count; k++) { if (people.Activity[k].Action == socialize && people.Activity[k].Phase == 1) { atFire[count++] = k; } }
            if (count >= 2 || minute > (3 * 1440) + (22 * 60))
            {
                var actor = count > 0 ? atFire[seed % count] : seed % people.Count;
                var target = count > 1 ? atFire[(seed + 1) % count] : (actor + 1) % people.Count;
                return (Rumors.Witness(world, predicate, actor, target, 1f, static (w, x, y) => InteractionSystem.InRange(w, x, y)), actor);
            }
        }
    }

    private static void StepTo(SimWorld world, long gameMs)
    {
        while (world.Clock.GameMs < gameMs) { world.Step(); }
    }
}
