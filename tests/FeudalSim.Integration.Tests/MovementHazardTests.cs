using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Health;
using FeudalSim.Sim.Survival;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-08: 11 §12.1 encumbrance, §12.2 swimming and drowning, §12.3 falling.</summary>
public sealed class MovementHazardTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld WithPlayer()
    {
        var w = (ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")) with { Player = [0f, 0f] }).CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private static void Move(SimWorld w, byte gait, int steps)
    {
        for (var s = 0; s < steps; s++)
        {
            w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Embodiment, new PlayerMoved(0.001f * (s % 2), 0f, 0f, gait)));
            w.Step();
        }
    }

    [Fact]
    public void Load_SetsTheState_AndTheSpeeds()   // 11 §12.1
    {
        var w = WithPlayer();
        var row = w.PlayerRow;
        var cap = Encumbrance.CapacityKg(w, row);
        cap.ShouldBe(20f + (4f * Sim.Skills.Skills.Attribute(w, row, "str")), 0.01f);
        Encumbrance.Of(0.4f).ShouldBe(Encumbrance.State.Free);
        Encumbrance.Of(0.8f).ShouldBe(Encumbrance.State.Burdened);
        Encumbrance.Of(1.2f).ShouldBe(Encumbrance.State.Overloaded);
        Encumbrance.Of(2f).ShouldBe(Encumbrance.State.Immobile);
        Encumbrance.Speed(1.6f, 4f, 6.5f, 2, 0.8f).ShouldBe(6.5f * 0.7f, 1e-4f);
        Encumbrance.Speed(1.6f, 4f, 6.5f, 1, 0.8f).ShouldBe(4f * 0.85f, 1e-4f);
        Encumbrance.Speed(1.6f, 4f, 6.5f, 2, 1.2f).ShouldBe(0.9f);
        Encumbrance.Tier(ActivityLevel.Light, 0.8f).ShouldBe(ActivityLevel.Moderate);
        Encumbrance.Tier(ActivityLevel.Light, 1.2f).ShouldBe(ActivityLevel.Heavy);

        var stone = Content.ItemHandle("item.flint_nodules");
        w.Inventory.Add(w.PlayerId, stone, (int)MathF.Round(cap * 0.75f / (float)Content.Items[stone].MassKg));   // ≈ 0.75 of capacity
        Encumbrance.Of(Encumbrance.Ratio(w, row)).ShouldBe(Encumbrance.State.Burdened);
        Move(w, 0, 5);
        w.People.Activity[row].Level.ShouldBe(ActivityLevel.Moderate);   // walking counts a tier up
    }

    [Fact]
    public void Falls_FollowTheTable()   // 11 §12.3
    {
        Falling.Effective(10f, 50f, 0f).ShouldBe(8f);
        Falling.Effective(3f, 0f, 0.5f).ShouldBe(5f);
        var (hurt, dead, n) = (0, 0, 0);
        for (var k = 0; k < 6; k++)
        {
            var w = WithPlayer();
            for (var s = 0; s < k * 13; s++) { w.Step(); }
            for (var row = 0; row < 24; row++)
            {
                Falling.Apply(w, row, 25f + (Content.SkillHandle("skill.athletics") is var a and >= 0 ? w.People.SkillLevels(row)[a] / 25f : 0f));   // ≥ 25 m effective
                n++;
                if (w.IsDead(row)) { dead++; } else if (w.Injuries.Of(w.People.Ids[row]).Count > 0) { hurt++; }
            }
        }

        ((float)dead / n).ShouldBeInRange(0.82f, 0.97f);   // > 20 m: 90 % fatal
        var low = WithPlayer();
        Falling.Apply(low, 3, 1.5f).ShouldBe(1.5f - (Content.SkillHandle("skill.athletics") is var b and >= 0 ? low.People.SkillLevels(3)[b] / 25f : 0f), 1e-4f);
        low.Injuries.Of(low.People.Ids[3]).ShouldBeEmpty();   // < 2 m: nothing
    }

    [Fact]
    public void Swimming_Drains3PerSecond_FloatingRests_EmptyMeansBreath_ThenDrowning()   // 11 §12.2
    {
        var w = WithPlayer();
        var row = w.PlayerRow;
        var athletics = Content.SkillHandle("skill.athletics");
        w.People.SkillLevels(row)[athletics] = 30;   // a fair swimmer
        var before = w.People.Stamina[row].Value;
        Move(w, 3, 10);   // 1 s of swimming
        w.People.Stamina[row].Swimming.ShouldBe((byte)1);
        (before - w.People.Stamina[row].Value).ShouldBe(3f, 0.35f);
        w.People.Body[row].Wetness.ShouldBeGreaterThan(90f);

        var floating = w.People.Stamina[row].Value;
        for (var s = 0; s < 30; s++) { w.Step(); }   // 3 s floating (the gait hold lapses: not moving)
        w.People.Stamina[row].Value.ShouldBeGreaterThan(floating);

        w.People.SkillLevels(row)[athletics] = 5;   // a poor swimmer flounders: ×2, and can't float
        w.People.Stamina[row].Value = 0f;
        var breath = StaminaSystem.BreathSeconds(Sim.Skills.Skills.Attribute(w, row, "end"));
        Move(w, 3, (int)((breath + 11f) * 10));
        w.People.Vitals[row].State.ShouldBe(VitalState.Downed);
        w.People.Vitals[row].Cause.ShouldBe(VitalCause.Drowning);
        for (var s = 0; s < 610 && !w.IsDead(row); s++) { w.Step(); }
        w.IsDead(row).ShouldBeTrue();
        w.People.Vitals[row].Cause.ShouldBe(VitalCause.Drowning);
    }

    [Fact]
    public void AHeavyLoad_SinksYou()   // 11 §12.2: r > 0.6 can't stay afloat
    {
        var w = WithPlayer();
        var row = w.PlayerRow;
        w.People.SkillLevels(row)[Content.SkillHandle("skill.athletics")] = 60;
        var cap = Encumbrance.CapacityKg(w, row);
        w.Inventory.Add(w.PlayerId, Content.ItemHandle("item.rough_log"), (int)MathF.Ceiling(cap * 0.7f / (float)Content.Items[Content.ItemHandle("item.rough_log")].MassKg));
        Move(w, 3, 50);
        w.People.Stamina[row].BreathUsed.ShouldBeGreaterThan(4f);   // full stamina, but the load drags them under
    }

    [Fact]
    public void TheSea_Is8_13_12_7ByMidSeason()   // 10 §6.4
    {
        var season = Sim.Time.GameDate.DaysPerSeason * 1440L;
        Sim.Climate.Weather.SeaTempC(season / 2).ShouldBe(8f, 0.01f);
        Sim.Climate.Weather.SeaTempC(season + (season / 2)).ShouldBe(13f, 0.01f);
        Sim.Climate.Weather.SeaTempC((2 * season) + (season / 2)).ShouldBe(12f, 0.01f);
        Sim.Climate.Weather.SeaTempC((3 * season) + (season / 2)).ShouldBe(7f, 0.01f);
    }

    [Fact]
    public void Dropping_MakesAPile_NearbyDropsMerge_PickingUpNeedsReach_AndPilesAreSaved()   // M2-08
    {
        var w = WithPlayer();
        var row = w.PlayerRow;
        var me = w.PlayerId;
        var log = Content.ItemHandle("item.rough_log");
        w.Inventory.Add(me, log, 3);
        Sim.World.PileStore.Drop(w, row, log, 2).ShouldBeNull();
        var pile = new Sim.Core.EntityId(w.Piles.All.ShouldHaveSingleItem().Key);
        w.Inventory.Count(pile, log).ShouldBe(2);
        w.Inventory.Count(me, log).ShouldBe(1);
        w.People.Transforms[row].X = 1f;   // within 1.5 m: the same pile
        Sim.World.PileStore.Drop(w, row, log, 1).ShouldBeNull();
        w.Piles.Count.ShouldBe(1);
        w.Inventory.Count(pile, log).ShouldBe(3);

        var image = Sim.Persistence.SaveCodec.Capture(w);
        var restored = Sim.Persistence.SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Piles.Count.ShouldBe(1);
        restored.Content = Content;
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));

        w.People.Transforms[row].X = 10f;
        Sim.World.PileStore.PickUp(w, row, pile, log, 1).ShouldBe("out of reach");
        w.People.Transforms[row].X = 0.5f;
        Sim.World.PileStore.PickUp(w, row, pile, log, 5).ShouldBeNull();   // takes what is there
        w.Inventory.Count(me, log).ShouldBe(3);
        w.Piles.Count.ShouldBe(0);   // an emptied pile is gone
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
