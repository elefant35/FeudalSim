using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Health;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-06a: 11 §4–§5.2, §5.4 and §14 against the doc's own anchors.</summary>
public sealed class HealthTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f] })
            .CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    /// <summary>Runs one person's health minute by minute (the system's own update), resting, fed and warm.</summary>
    private long _offset;   // minutes past the clock this test has simulated (xunit: one instance per test)

    private void Hours(SimWorld w, int row, float hours)
    {
        for (var m = 1; m <= (int)MathF.Round(hours * 60); m++)
        {
            w.People.Needs[row] = Needs.Full;
            w.People.Activity[row].Level = ActivityLevel.Rest;
            HealthSystem.Update(w, row, 1f / 60f, w.Clock.GameMinute + ++_offset);
        }
    }

    private static Injury Wound(SimWorld w, InjuryType type, float severity, float bleed, BodyRegion region, byte flags = 0)
        => new() { Id = w.Injuries.NextId(), CreatedMin = w.Clock.GameMinute, Severity = severity, BleedRate = bleed, Region = region, Type = type, Flags = flags };

    [Fact]
    public void WolfBite_WorkedExample()   // 11 §4.4
    {
        var w = Camp();
        const int row = 3;
        HealthRules.Trauma(w, row, 26f, DamageType.Pierce, BodyRegion.LegL, TraumaSource.Animal).ShouldBeGreaterThan(0UL);
        HealthRules.Trauma(w, row, 5f, DamageType.Cut, BodyRegion.ArmR, TraumaSource.Animal).ShouldBe(0UL);   // < 8: the Bruise pool
        HealthSystem.Update(w, row, 0f, w.Clock.GameMinute);
        w.People.Vitals[row].Health.ShouldBe(69f, 0.01f);
        var bite = w.Injuries.Of(w.People.Ids[row]).Single();
        (bite.Type, bite.BleedRate, bite.Contamination, bite.Open).ShouldBe((InjuryType.Puncture, 5f, 0.8f, true));

        Hours(w, row, 2f);   // unbandaged: 5 the first hour, then ×0.75
        w.People.Vitals[row].Blood.ShouldBe(91.25f, 0.05f);
        w.People.Vitals[row].Health.ShouldBe(67f, 1.5f);   // 67 in the doc, which ignores two hours' healing
        HealthRules.Mobility([bite with { Severity = 26f }]).ShouldBe(0.818f, 0.001f);
        HealthRules.MoveSpeedMult([bite with { Severity = 26f }], 91f).ShouldBe(0.87f, 0.005f);
    }

    [Fact]
    public void AnArterialBleed_KillsInAbout2_5Hours_AndDownsBeforeBloodReaches35()   // 11 §2.3 (finding 11 Q9)
    {
        var w = Camp();
        const int row = 5;
        var id = w.People.Ids[row];
        w.Injuries.Add(id, Wound(w, InjuryType.Cut, 55f, HealthRules.ArterialBleed, BodyRegion.ArmL, Injury.OpenFlag | Injury.ArterialFlag));
        var (downAt, deadAt) = (-1f, -1f);
        var now = w.Clock.GameMinute;
        for (var m = 1; m <= 200 && deadAt < 0f; m++)
        {
            w.People.Needs[row] = Needs.Full;
            HealthSystem.Update(w, row, 1f / 60f, now + m);
            if (downAt < 0f && w.IsDown(row)) { downAt = m / 60f; }
            if (w.IsDead(row)) { deadAt = m / 60f; }
        }

        // §2.3 says Downed ≈ 1.6 h (Blood < 35), but an arterial cut is Critical (≥ 55), so Health ≤ 0 comes first:
        // 100 − 55 − 0.8·(100 − Blood) ≤ 0 at Blood ≈ 44 → ≈ 1.4 h. Death at Blood 0 is as written: 2.5 h.
        downAt.ShouldBeInRange(1.35f, 1.62f);
        deadAt.ShouldBe(2.5f, 0.05f);
        (w.People.Vitals[row].State, w.People.Vitals[row].Cause).ShouldBe((VitalState.Dead, VitalCause.BloodLoss));
        w.People.Activity[row].Action.ShouldBe((short)-1);
    }

    [Fact]
    public void BloodRegenerates_65_InAbout3_4Days()   // 11 §5.2
    {
        var w = Camp();
        const int row = 6;
        w.People.Vitals[row].Blood = 35f;
        var now = w.Clock.GameMinute;
        var hours = 0;
        while (w.People.Vitals[row].Blood < 100f && hours < 200)
        {
            w.People.Needs[row] = Needs.Full;
            w.People.Activity[row].Level = ActivityLevel.Light;   // up and about: no bed-rest bonus
            HealthSystem.Update(w, row, 1f, now + (++hours * 60));
        }

        (hours / 24f).ShouldBe(3.4f, 0.05f);
    }

    [Fact]
    public void Healing_TheSevereArmCut()   // 11 §5.4
    {
        // Untreated, normal work, fed, adult, END 6: M = 0.6 × 0.75 × 1.04 = 0.468 → 3.2 days (doc).
        var m = HealthRules.HealMultiplier(ActivityLevel.Moderate, 80f, false, false, 6f, 80f);
        m.ShouldBe(0.468f, 0.001f);
        (50f / (50f / HealthRules.BaseHealDays(InjuryType.Cut) * m)).ShouldBe(3.2f, 0.05f);
        // Stitched by a Journeyman (q 0.7 → treatment 1.02), light work: M = 1.06 → 1.4 days (doc).
        var treated = HealthRules.HealMultiplier(ActivityLevel.Light, 80f, false, false, 6f, 80f, treatment: 0.6f + (0.6f * 0.7f));
        (50f / (50f / HealthRules.BaseHealDays(InjuryType.Cut) * treated)).ShouldBe(1.4f, 0.05f);
    }

    [Fact]
    public void Downed_StableForTwoHours_ComesRound_WithHealthAtLeast5()   // 11 §14
    {
        var w = Camp();
        const int row = 7;
        w.People.Vitals[row].Bruise = 100f;   // a knockout-sized battering: Health 0
        w.Injuries.Add(w.People.Ids[row], Wound(w, InjuryType.Bruise, 10f, 0f, BodyRegion.Torso));
        Hours(w, row, 0.02f);
        w.People.Vitals[row].State.ShouldBe(VitalState.Downed);
        w.CanAct(row).ShouldBeFalse();
        Hours(w, row, 1.5f);
        w.IsDown(row).ShouldBeTrue();          // not before two stable hours
        Hours(w, row, 0.6f);
        w.CanAct(row).ShouldBeTrue();
        w.People.Vitals[row].Health.ShouldBeGreaterThanOrEqualTo(5f);
    }

    [Fact]
    public void TheDead_AreSkippedByEverySystem_AndTheDownHoldNoAction()
    {
        var w = Camp();
        const int dead = 8, down = 9;
        w.People.Vitals[dead].Blood = 0f;
        HealthSystem.Update(w, dead, 0f, w.Clock.GameMinute);
        w.IsDead(dead).ShouldBeTrue();
        w.People.Vitals[down].Bruise = 100f;
        w.Injuries.Add(w.People.Ids[down], Wound(w, InjuryType.Bruise, 10f, 0f, BodyRegion.Torso));
        var needs = w.People.Needs[dead];
        var events = new List<object>();
        for (var s = 0; s < 3000; s++) { events.AddRange(w.Step().Events.Select(e => e.Payload)); }
        w.People.Needs[dead].ShouldBe(needs);   // a body integrates nothing
        w.People.Activity[dead].Action.ShouldBe((short)-1);
        var downId = w.People.Ids[down];
        events.OfType<PersonDowned>().Any(e => e.Person == downId).ShouldBeTrue();
        w.Conversations.Count.ShouldBe(0);
    }

    [Fact]
    public void TraumaCommand_IsScenarioOnly_AndInjuriesSurviveSaveAndLoad()
    {
        var w = Camp();
        var id = w.People.Ids[2];
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new InflictTrauma(id, 40f, (byte)DamageType.Cut, (byte)BodyRegion.ArmR, (byte)TraumaSource.Tool)));
        w.Step().Events.Select(e => e.Payload).OfType<CommandRejected>().ShouldNotBeEmpty();
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Scenario, new InflictTrauma(id, 40f, (byte)DamageType.Cut, (byte)BodyRegion.ArmR, (byte)TraumaSource.Tool)));
        w.Step();
        w.Injuries.Of(id).Single().BleedRate.ShouldBe(10f);   // Severe cut
        w.People.Vitals[11].Blood = 0f;
        HealthSystem.Update(w, 11, 0f, w.Clock.GameMinute);

        var image = SaveCodec.Capture(w);
        for (var s = 0; s < 1500; s++) { w.Step(); }
        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        for (var s = 0; s < 1500; s++) { restored.Step(); }
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
    }

    [Fact]
    public void TraumaTyping_IsDeterministic_AndFollowsTheTable()
    {
        var w = Camp();
        var fractures = 0;
        for (var k = 0; k < 400; k++)
        {
            var row = k % 20;
            var id = HealthRules.Trauma(w, row, 40f, DamageType.Blunt, BodyRegion.LegR, TraumaSource.Tool);
            if (w.Injuries.Of(w.People.Ids[row]).Single(i => i.Id == id).Type == InjuryType.Fracture) { fractures++; }
        }

        (fractures / 400f).ShouldBe(0.5f, 0.08f);   // p = clamp((40 − 25)/30) = 0.5
        HealthRules.Tier(19.9f).ShouldBe(0);
        HealthRules.Tier(55f).ShouldBe(3);
        HealthRules.Penalties([], 100f).ShouldBe(new HealthRules.CombatPenalties(1f, 1f, 1f, 1f, 0f, true));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
