using System.Diagnostics;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Integration.Tests;

/// <summary>M0-09 (20 §20 step 9): hosting.</summary>
public class HostingTests
{
    private static SimWorld Camp(int people = 24)
    {
        var world = new SimWorld(42).AddSystem(new WanderSystem()).AddSystem(new NeedsDecaySystem());
        for (var i = 0; i < people; i++)
        {
            world.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"S{i}", i, 0)));
        }

        return world;
    }

    [Fact]
    public void Triple_buffer_never_yields_a_torn_snapshot_under_contention()
    {
        var buffer = new TripleBuffer<RenderSnapshot>(() => new RenderSnapshot { Count = 100, X = new float[100] });
        const int writes = 200_000;
        var writer = new Thread(() =>
        {
            for (var k = 1; k <= writes; k++)
            {
                var s = buffer.Back;
                s.Step = k;
                s.Count = 100;
                for (var i = 0; i < 100; i++) { s.X[i] = k; }
                buffer.Publish();
            }
        });

        var torn = 0;
        var lastSeen = 0L;
        var backwards = 0;
        writer.Start();
        while (writer.IsAlive || lastSeen < writes)
        {
            var s = buffer.ReadLatest();
            for (var i = 0; i < s.Count; i++)
            {
                if (s.X[i] != s.Step) { torn++; break; }
            }

            if (s.Step < lastSeen) { backwards++; }
            lastSeen = Math.Max(lastSeen, s.Step);
            if (!writer.IsAlive && lastSeen < writes && buffer.ReadLatest().Step == lastSeen) { break; }
        }

        writer.Join();
        torn.ShouldBe(0);
        backwards.ShouldBe(0);
        buffer.ReadLatest().Step.ShouldBe(writes);
    }

    [Fact]
    public void Pause_step_and_resume_through_dev_commands()
    {
        using var runner = new SimRunner(Camp());
        var dev = new DevCommands(runner);
        dev.Count.ShouldBeGreaterThanOrEqualTo(5);
        dev.Execute("timescale 4");
        Thread.Sleep(400);
        dev.Execute("pause").ShouldBe("paused");
        Thread.Sleep(100);
        var paused = runner.StepsExecuted;
        paused.ShouldBeGreaterThan(5);
        Thread.Sleep(300);
        runner.StepsExecuted.ShouldBe(paused);

        dev.Execute("step 5");
        runner.StepsExecuted.ShouldBe(paused + 5);

        dev.Execute("resume").ShouldBe("running");
        Thread.Sleep(300);
        runner.StepsExecuted.ShouldBeGreaterThan(paused + 5);
    }

    [Fact]
    public async Task State_changing_dev_commands_are_logged_and_applied()
    {
        using var runner = new SimRunner(Camp(), mode: RunMode.Paused);
        var dev = new DevCommands(runner);
        dev.Execute("spawn Bram 3 4");
        dev.Execute("daylength 60");
        dev.Execute("step 2");
        (await runner.Invoke(w => w.People.Count)).ShouldBe(25);
        (await runner.Invoke(w => w.Clock.DayLengthMinutes)).ShouldBe(60);
        dev.Execute("hash").ShouldStartWith("hash ");
        dev.Execute("time").ShouldContain("Y0 Spring 1");
    }

    [Fact]
    public void Realtime_holds_ten_steps_per_second()
    {
        using var runner = new SimRunner(Camp());
        var sw = Stopwatch.StartNew();
        Thread.Sleep(3_000);
        var rate = runner.StepsExecuted / sw.Elapsed.TotalSeconds;
        rate.ShouldBeInRange(9.0, 11.0);
        runner.TimeDilationEvents.ShouldBe(0);
    }

    [Fact]
    public async Task Snapshots_and_events_reach_presentation()
    {
        using var runner = new SimRunner(Camp(), mode: RunMode.Paused);
        (await runner.StepWhilePaused(3)).ShouldBe(3);
        var snapshot = runner.Snapshots.ReadLatest();
        snapshot.Step.ShouldBe(3);
        snapshot.Count.ShouldBe(24);
        var spawned = 0;
        while (runner.Events.TryPop(out var e)) { if (e.Payload is Sim.Events.PersonSpawned) { spawned++; } }
        spawned.ShouldBe(24);
    }
}
