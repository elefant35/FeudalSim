using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Social;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-29 (30 §5 owner example): theft → witnesses → wariness and rumor, through the sim's own rules.</summary>
public sealed class TheftTests(ITestOutputHelper log)
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static readonly List<StepOutput> Outputs = [];

    private static readonly HeadlessBodies Bodies = new();   // LOD0 settlers near the player need bodies to move (ADR-0007)

    private static void Run(SimWorld w, int steps, params StateCommand[] commands)
    {
        foreach (var c in commands) { w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, c)); }
        for (var i = 0; i < steps; i++)
        {
            foreach (var r in Bodies.Step(w)) { w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Embodiment, r)); }
            var o = w.Step(); if (o.Events.Count > 0) { Outputs.Add(o); }
            foreach (var dp in o.OpenedDecisions) { w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Integrity, dp)); }
        }
    }

    [Fact]
    public void ATheftAtTheFire_IsSeen_TheVictimHoldsItAgainstYou_WitnessesGrowWary_AndTheRumorSpreads()
    {
        var talk = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_talk.yaml"));
        var w = talk.CreateWorld(Content, SerialJobScheduler.Instance);
        Run(w, 20);
        const int victimRow = 4;
        var victim = w.People.Ids[victimRow];
        var at = w.People.Transforms[victimRow];
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Scenario, new SetHoldings(victim, "item.iron_knife", 1, 0)));
        Run(w, 1, new PlayerMoved(at.X + 1f, at.Z, 0f));
        Run(w, 2, new StartConversation(victim));   // they're looking at you
        var trustBefore = w.Relationships.Trust(victim, w.PlayerId);
        var opinionBefore = w.Relationships.Opinion(victim, w.PlayerId);
        Run(w, 1, new Steal(victim, "item.iron_knife", 1));

        var theft = Outputs.SelectMany(o => o.Events).Select(e => e.Payload).OfType<TheftCommitted>().Single();
        theft.Seen.ShouldBeGreaterThan(0);
        var knife = Content.Items.ToList().FindIndex(i => i.Id == "item.iron_knife");
        w.Holdings.Goods(w.PlayerId, knife).ShouldBe(1);
        w.Holdings.Goods(victim, knife).ShouldBe(0);
        Theft.Wary(w, victim, w.PlayerId).ShouldBeTrue();                                   // the victim saw it
        w.Relationships.Opinion(victim, w.PlayerId).ShouldBeLessThan(opinionBefore - 5f);    // stole_from_me
        w.Relationships.Trust(victim, w.PlayerId).ShouldBeLessThan(trustBefore - 10f);
        int Believers() => Enumerable.Range(0, w.People.Count).Count(k => k != w.PlayerRow && Theft.Wary(w, w.People.Ids[k], w.PlayerId)
            || BelievesAtAll(w, w.People.Ids[k]));
        var witnesses = Believers();
        witnesses.ShouldBe(theft.Seen);

        Run(w, 2 * 18_000);   // two game days of gossip
        var later = Believers();
        log.WriteLine($"theft: seen by {theft.Seen} (victim wary: yes); after 2 days {later} of {w.People.Count - 1} believe it");
        later.ShouldBeGreaterThan(witnesses);                                         // the rumor spread beyond who saw it
        w.Reject(new Steal(victim, "item.iron_knife", 1)).ShouldBeTrue();                   // nothing left to take
    }

    private static bool BelievesAtAll(SimWorld w, Sim.Core.EntityId holder)
    {
        var p = Content.ClaimHandle("claim.stole");
        foreach (ref readonly var b in w.Beliefs.Span(holder)) { if (w.Claims[b.Claim].Predicate == p && w.Claims[b.Claim].Subject == w.PlayerId.Value && b.C > 0.05f) { return true; } }
        return false;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}

internal static class RejectProbe
{
    /// <summary>True if the sim rejects the command (applied in one step).</summary>
    public static bool Reject(this SimWorld w, StateCommand c)
    {
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, c));
        return w.Step().Events.Any(e => e.Payload is CommandRejected);
    }
}
