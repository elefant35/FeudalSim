using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Health;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-06b: 11 §5.2–5.3 treatment and infection against the doc's anchors.</summary>
public sealed class TreatmentTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp()
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [12f, -6f] })
            .CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private static Injury Wound(InjuryType type, float sev, float bleed, float c, ulong id = 1)
        => new() { Id = id, Severity = sev, BleedRate = bleed, Contamination = c, Type = type, Region = BodyRegion.LegL, Flags = Injury.OpenFlag, TourniquetMin = -1 };

    [Fact]
    public void AnUntreatedBite_IsInfectedAbout57PercentOfTheTime_OverTenSlots()   // 11 §5.3 check
    {
        var w = Camp();
        var id = w.People.Ids[1];
        var infected = 0;
        const int n = 4000;
        for (var k = 0; k < n; k++)
        {
            var bite = Wound(InjuryType.Puncture, 26f, 0f, 0.8f, (ulong)(k + 1));
            for (var slot = 0; slot < 10 && bite.Infection == InfectionState.Clean; slot++) { Treatment.Slot(w, id, ref bite, slot, 1f, 5f, bedRest: true); }
            if (bite.Infection != InfectionState.Clean) { infected++; }
        }

        (infected / (double)n).ShouldBe(0.566, 0.03);

        // Cleaned with boiled water (q 0.7) and dressed with honey: c = 0.8 × (1 − 0.49) × 0.5 = 0.204 by the formulas
        // (the doc's prose says 0.12 → ≈ 11 %; finding 11 Q10). Ten slots: ≈ 18.6 %.
        var dressed = Wound(InjuryType.Puncture, 26f, 0f, 0.8f);
        Treatment.Apply(w, id, ref dressed, Procedure.Clean, 0.7f);
        Treatment.Apply(w, id, ref dressed, Procedure.Honey, 0.7f);
        dressed.Contamination.ShouldBe(0.204f, 0.001f);
    }

    [Fact]
    public void Untreated_Infection_ReachesInfectedSepticAndDeath_OnTheDocsTimeline()   // 11 §5.3: ~1 / ~2.2 / ~3 days
    {
        // ΔI = 16 − (2 + 0.8·5 + 2) = +8 per slot at END 5 on bed rest.
        Treatment.Progression(Wound(InjuryType.Puncture, 26f, 0f, 0.8f), 5f, bedRest: true, nursed: false).ShouldBe(8f);
        var poultice = Wound(InjuryType.Puncture, 26f, 0f, 0.8f) with { Treated = Treated.Poultice, PoulticeQ = 0.8f };
        Treatment.Progression(poultice, 5f, bedRest: true, nursed: true).ShouldBe(0.8f, 0.001f);   // "about +1 per slot"

        var w = Camp();
        const int row = 4;
        var id = w.People.Ids[row];
        w.Injuries.Add(id, Wound(InjuryType.Puncture, 26f, 0f, 0.8f) with { Id = w.Injuries.NextId(), CreatedMin = w.Clock.GameMinute, Infection = InfectionState.Inflamed });
        var (infectedAt, septicAt, downAt, deadAt) = (-1f, -1f, -1f, -1f);
        var now = w.Clock.GameMinute;
        for (var h = 1; h <= 24 * 5 && deadAt < 0f; h++)
        {
            w.People.Needs[row] = Needs.Full;
            w.People.Activity[row].Level = ActivityLevel.Rest;
            HealthSystem.Update(w, row, 1f, now + (h * 60));
            var worst = w.Injuries.Of(id).Max(i => i.Infection);
            if (infectedAt < 0f && worst >= InfectionState.Infected) { infectedAt = h / 24f; }
            if (septicAt < 0f && worst == InfectionState.Septic) { septicAt = h / 24f; }
            if (downAt < 0f && w.IsDown(row)) { downAt = h / 24f; }
            if (w.IsDead(row)) { deadAt = h / 24f; }
        }

        infectedAt.ShouldBeInRange(0.75f, 1.5f);
        septicAt.ShouldBeInRange(1.75f, 2.75f);
        if (downAt >= 0f) { downAt.ShouldBeGreaterThanOrEqualTo(septicAt); }   // Septic ≥ 90 downs — unless one slot jumps from < 90 past 100
        deadAt.ShouldBeInRange(2.75f, 3.75f);
        w.People.Vitals[row].Cause.ShouldBe(VitalCause.Infection);
    }

    [Fact]
    public void Procedures_HaveTheirEffects()   // 11 §5.2 table
    {
        var w = Camp();
        var id = w.People.Ids[1];
        var cut = Wound(InjuryType.Cut, 40f, 10f, 0.5f);
        Treatment.Apply(w, id, ref cut, Procedure.Bandage, 0.5f);
        cut.BleedRate.ShouldBe(10f * (0.4f - 0.15f), 0.001f);   // Severe ×(0.4 − 0.3q)
        var minor = Wound(InjuryType.Cut, 25f, 4f, 0.5f);
        Treatment.Apply(w, id, ref minor, Procedure.Bandage, 0.2f);
        minor.BleedRate.ShouldBe(0f);                           // Moderate stops
        Treatment.Apply(w, id, ref cut, Procedure.Stitch, 0.7f);
        cut.BleedRate.ShouldBe(0f);
        Treatment.HealFactor(cut).ShouldBe((0.6f + (0.6f * 0.7f)) * 1.3f, 0.001f);   // treated q 0.7, stitched ×1.3

        var septic = Wound(InjuryType.Puncture, 30f, 12f, 0.8f, 7) with { Infection = InfectionState.Infected, InfectionSev = 50f };
        var before = w.Injuries.Of(id).Count;
        Treatment.Apply(w, id, ref septic, Procedure.Cautery, 0.6f);
        (septic.BleedRate, septic.Infection, septic.InfectionSev).ShouldBe((0f, InfectionState.Clean, 0f));
        w.Injuries.Of(id).Count.ShouldBe(before + 1);   // the cautery burn
        w.Injuries.Of(id)[^1].Type.ShouldBe(InjuryType.Burn);

        var fracture = new Injury { Type = InjuryType.Fracture, Severity = 40f, Region = BodyRegion.LegR, TourniquetMin = -1 };
        HealthRules.Mobility([fracture]).ShouldBe(0.4f, 0.001f);   // 1 − 40·1.5/100
        Treatment.Apply(w, id, ref fracture, Procedure.SetAndSplint, 0.8f);
        HealthRules.Mobility([fracture]).ShouldBe(0.76f, 0.001f);  // splinted 0.6
        Treatment.Problem(Procedure.Stitch, Wound(InjuryType.Cut, 40f, 10f, 0.5f), healing: 10).ShouldBe("needs Healing 20");
        Treatment.Problem(Procedure.SetAndSplint, Wound(InjuryType.Cut, 40f, 10f, 0.5f), healing: 60).ShouldBe("only fractures are set");
    }

    [Fact]
    public void TheTreatCommand_ChecksTheHealer_AndUsesTheirHealingCheck()
    {
        var w = Camp();
        var player = w.PlayerRow;
        var patient = 0;
        var pid = w.People.Ids[patient];
        HealthRules.Trauma(w, patient, 40f, DamageType.Cut, BodyRegion.ArmR, TraumaSource.Tool);
        var wound = w.Injuries.Of(pid).Single().Id;
        var healing = Content.SkillHandle("skill.healing");
        w.People.SkillLevels(player)[healing] = 22;   // stitches (20), not cautery (25)

        Submit(w, CommandSource.Player, new TreatWound(w.People.Ids[3], pid, wound, (byte)Procedure.Bandage)).ShouldContain("only treat as themselves");
        Submit(w, CommandSource.Player, new TreatWound(w.PlayerId, pid, wound, (byte)Procedure.Bandage)).ShouldContain("out of reach");
        var t = w.People.Transforms[patient];
        Submit(w, CommandSource.Embodiment, new PlayerMoved(t.X + 1f, t.Z, 0f));
        Submit(w, CommandSource.Player, new TreatWound(w.PlayerId, pid, wound, (byte)Procedure.Cautery)).ShouldContain("needs Healing 25");
        var events = new List<object>();
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new TreatWound(w.PlayerId, pid, wound, (byte)Procedure.Stitch)));
        events.AddRange(w.Step().Events.Select(e => e.Payload));
        var treated = events.OfType<WoundTreated>().Single();
        if (treated.Applied)
        {
            treated.Q.ShouldBeInRange(0f, 1f);
            var after = w.Injuries.Of(pid).Single(i => i.Id == wound);
            (after.BleedRate, (after.Treated & Treated.Stitched) != 0).ShouldBe((0f, true));
        }
    }

    private static string Submit(SimWorld w, CommandSource source, StateCommand c)
    {
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, source, c));
        return string.Join(" | ", w.Step().Events.Select(e => e.Payload).OfType<CommandRejected>().Select(r => r.Reason));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
