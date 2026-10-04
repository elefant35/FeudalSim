using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// M1-05 social core in the camp (16 §4): seeds shipmate edges at Landfall; every game quarter-hour, each person
/// talking at the fire chats with one co-present partner (`chatted`, ×1.5 when lonely; social contact w 3) and people
/// eating together share a meal (`shared_meal`); each game hour, pairs on the same purposeful task count toward
/// co-working (w 1.5 after 2 h); daily, co-residence (w 0.3 in camps ≤ 60) and the relationship update.
/// Chat partner choice is a keyed draw (no RNG state). The interaction catalog proper (M1-06) replaces the chat stand-in.
/// </summary>
public sealed class SocialSystem : ISimSystem
{
    private ContentDatabase? _cachedFor;
    private int _socialize = -1, _eat = -1;

    public string Name => "Social";
    public SimPhase Phase => SimPhase.World;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var people = world.People;
        if (world.Camp.Active == 0 || people.Count == 0) { return; }
        if (!ReferenceEquals(_cachedFor, world.Content))
        {
            _cachedFor = world.Content;
            _socialize = ContentDatabase.HandleOf(world.Content.Actions, "action.socialize", a => a.Id);
            _eat = ContentDatabase.HandleOf(world.Content.Actions, "action.eat_meal", a => a.Id);
        }

        var rel = world.Relationships;
        if (!rel.ShipmatesSeeded) { rel.SeedShipmates(); }

        var prevMinute = (ctx.GameMs - ctx.DtGameMs) / 60_000;
        var minute = ctx.GameMs / 60_000;
        if (minute / 15 != prevMinute / 15) { QuarterHour(world, minute / 15); }
        if (minute / 60 != prevMinute / 60) { Hour(world); }
        if (minute / 1440 != prevMinute / 1440) { Day(world); }
    }

    private void QuarterHour(SimWorld world, long quarter)
    {
        var people = world.People;
        Span<int> group = stackalloc int[people.Count];
        foreach (var (action, modifier, social) in new[] { (_eat, "opinion.shared_meal", false) })   // talk itself: InteractionSystem (M1-06)
        {
            if (action < 0) { continue; }
            var n = 0;
            for (var i = 0; i < people.Count; i++)
            {
                ref readonly var a = ref people.Activity[i];
                if (a.Action == action && a.Phase == 1) { group[n++] = i; }
            }

            if (n < 2) { continue; }
            for (var k = 0; k < n; k++)
            {
                var i = group[k];
                var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Social, people.Ids[i].Value, (ulong)quarter, Salt.ChatPartner + (social ? 0u : 100u)));
                var pick = rng.Range(0, n - 1);
                var j = group[pick >= k ? pick + 1 : pick];
                foreach (var (holder, other) in new[] { (i, j), (j, i) })
                {
                    var lonely = social && people.Needs[holder].Social < 40f ? 1.5f : 1f;
                    world.Relationships.ApplyModifier(people.Ids[holder], people.Ids[other], modifier, lonely);
                }

                if (social) { world.Relationships.Contact(people.Ids[i], people.Ids[j], 3f, social: true); }
            }
        }
    }

    private static void Hour(SimWorld world)
    {
        var people = world.People;
        for (var i = 0; i < people.Count; i++)
        {
            ref readonly var a = ref people.Activity[i];
            if (a.Phase != 1 || !a.Has(ActivityState.Purposeful)) { continue; }
            for (var j = i + 1; j < people.Count; j++)
            {
                ref readonly var b = ref people.Activity[j];
                if (b.Phase == 1 && b.Action == a.Action) { world.Relationships.CoWorkHour(people.Ids[i], people.Ids[j]); }
            }
        }
    }

    private static void Day(SimWorld world)
    {
        var people = world.People;
        if (people.Count <= 60)
        {
            for (var i = 0; i < people.Count; i++)
            {
                for (var j = i + 1; j < people.Count; j++) { world.Relationships.Contact(people.Ids[i], people.Ids[j], 0.3f, social: false); }
            }
        }

        world.Memories.Compact(world.Clock.GameMinute);
        world.Beliefs.Forget();
        world.Relationships.DailyUpdate();
    }
}
