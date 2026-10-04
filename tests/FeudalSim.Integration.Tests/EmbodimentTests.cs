using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>
/// M0-13 (20 §20 step 13, ADR-0007) headless: a fake client plays Godot's part — reporting the player's
/// position and moving embodied bodies toward their sim-chosen targets — and the session replays exactly.
/// </summary>
public class EmbodimentTests
{
    private static SimWorld World()
    {
        var w = new SimWorld(5).AddSystem(new LodSystem()).AddSystem(new WanderSystem()).AddSystem(new NeedsDecaySystem());
        w.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("Hedda", 0, 0)));
        return w;
    }

    /// <summary>A stand-in for GodotEmbodiment: snaps a newly embodied body 0.4 m off the sim pose, then walks it at 1.6 m/s.</summary>
    private sealed class FakeClient
    {
        private readonly Dictionary<ulong, (float X, float Z)> _bodies = [];
        private long _seq = 100;

        public List<CommandEnvelope> Commands(SimWorld w, float playerX, float playerZ)
        {
            var list = new List<CommandEnvelope> { new(++_seq, 0, CommandSource.Embodiment, new PlayerMoved(playerX, playerZ, 0)) };
            var p = w.People;
            for (var i = 0; i < p.Count; i++)
            {
                var id = p.Ids[i];
                if (p.Lod[i].Tier != LodTier.Lod0) { _bodies.Remove(id.Value); continue; }
                if (!_bodies.TryGetValue(id.Value, out var body)) { body = (p.Transforms[i].X + 0.4f, p.Transforms[i].Z); }   // navmesh snap
                else if (p.Wander[i].HasTarget)
                {
                    var dx = p.Wander[i].TargetX - body.X;
                    var dz = p.Wander[i].TargetZ - body.Z;
                    var d = MathF.Sqrt((dx * dx) + (dz * dz));
                    var stepLen = MathF.Min(d, 0.16f);
                    if (d > 0.001f) { body = (body.X + (dx / d * stepLen), body.Z + (dz / d * stepLen)); }
                }

                _bodies[id.Value] = body;
                list.Add(new(++_seq, 0, CommandSource.Embodiment, new EmbodimentReport(id, body.X, body.Z, 0)));
            }

            return list;
        }
    }

    [Fact]
    public void Embodiment_hysteresis_snap_and_exact_replay()
    {
        var live = World();
        var client = new FakeClient();
        var log = new List<CommandEnvelope>();
        var events = new List<string>();
        var step = 0;

        void Run(int steps, float px, float pz)
        {
            for (var i = 0; i < steps; i++)
            {
                foreach (var c in client.Commands(live, px, pz)) { live.Enqueue(c); }
                var output = live.Step();
                log.AddRange(output.AppliedCommands);
                events.AddRange(output.Events.Select(e => $"{e.Step}:{e.Payload}"));
                step++;
            }
        }

        Run(200, 0, 10);       // player nearby: embodied, body walks to sim targets
        events.ShouldContain(e => e.Contains("LodChanged") && e.Contains("To = Lod0"));
        var snap = events.First(e => e.Contains("Embodied"));
        snap.ShouldContain("SnapDistance = 0.4");
        var bodyMoved = live.People.Transforms[0];
        (MathF.Abs(bodyMoved.X) + MathF.Abs(bodyMoved.Z)).ShouldBeGreaterThan(0.5f);   // the client's body moved the sim pose

        Run(40, 150, 0);       // far, but < 5 s: still embodied
        live.People.Lod[0].Tier.ShouldBe(LodTier.Lod0);
        Run(20, 150, 0);       // > 5 s beyond 100 m: demoted to a puppet
        live.People.Lod[0].Tier.ShouldBe(LodTier.Lod1);
        var demotedAt = events.Where(e => e.Contains("To = Lod1")).Select(e => long.Parse(e.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture)).Single();
        demotedAt.ShouldBeGreaterThanOrEqualTo(200 + LodSystem.DemoteAfterSteps);

        Run(100, 0, 5);        // back within 80 m: re-embodied, snap < 1 m
        events.Count(e => e.Contains("To = Lod0")).ShouldBe(2);
        var reSnap = float.Parse(events.Last(e => e.Contains("Embodied")).Split("SnapDistance = ")[1].TrimEnd(' ', '}'), System.Globalization.CultureInfo.InvariantCulture);
        reSnap.ShouldBeLessThan(1f);

        var liveHash = StateHasher.Hash(live);
        var replay = World();
        var replayed = new List<string>();
        Replayer.Run(replay, log.Where(c => c.Source != CommandSource.Scenario).ToList(), live.Clock.Step, o => replayed.AddRange(o.Events.Select(e => $"{e.Step}:{e.Payload}")));
        StateHasher.Hash(replay).ShouldBe(liveHash);
        replayed.ShouldBe(events);
    }
}
