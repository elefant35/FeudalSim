using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Sim.Tests;

public class WorldTests
{
    internal static SimWorld NewCamp(ulong seed, int people = 24)
    {
        var world = new SimWorld(seed, startGameMs: (5 * 3_600_000) + (30 * 60_000))   // Landfall 05:30
            .AddSystem(new WanderSystem())
            .AddSystem(new NeedsDecaySystem());
        for (var i = 0; i < people; i++)
        {
            world.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"Settler {i + 1}", i % 6 * 4f, i / 6 * -4f)));
        }

        return world;
    }

    [Fact]
    public void Spawn_commands_create_people_in_id_order()
    {
        var world = NewCamp(7);
        var output = world.Step();
        world.People.Count.ShouldBe(24);
        output.AppliedCommands.Count.ShouldBe(24);
        output.AppliedCommands.ShouldAllBe(c => c.ApplyStep == 1);
        output.Events.Count(e => e.Payload is PersonSpawned).ShouldBe(24);
        for (var i = 1; i < world.People.Count; i++) { world.People.Ids[i].CompareTo(world.People.Ids[i - 1]).ShouldBePositive(); }
    }

    [Fact]
    public void People_wander_and_needs_decay_over_a_game_hour()
    {
        var world = NewCamp(7);
        for (var i = 0; i < 751; i++) { world.Step(); }   // 1 + 750 steps = spawn + one game hour
        world.People.Count.ShouldBe(24);
        var moved = 0;
        for (var i = 0; i < world.People.Count; i++)
        {
            var t = world.People.Transforms[i];
            var w = world.People.Wander[i];
            if (MathF.Abs(t.X - w.HomeX) + MathF.Abs(t.Z - w.HomeZ) > 0.01f) { moved++; }
            world.People.Needs[i].Satiety.ShouldBe(100 - NeedsDecaySystem.SatietyPerHour, 0.01f);
        }

        moved.ShouldBeGreaterThan(12);
    }

    [Fact]
    public void Crossing_midnight_emits_DayStarted()
    {
        var world = NewCamp(7, people: 1);
        var days = 0;
        for (var i = 0; i < 18_000; i++) { days += world.Step().Events.Count(e => e.Payload is DayStarted); }
        days.ShouldBe(1);
    }
}
