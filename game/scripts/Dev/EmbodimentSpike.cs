using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using Godot;

namespace FeudalSim.Game.Dev;

/// <summary>
/// M0-13: the embodiment boundary (ADR-0007, 20 §3) in Godot. The sim owns intent (who is LOD0, where they want
/// to go); Godot moves embodied NPCs with NavigationAgent3D and reports their poses back as logged commands.
/// Non-embodied NPCs are puppets drawn at the sim's pose. `-- --autotest` walks the player 150 m away and back,
/// prints LOD/embodiment events, the final state hash and the input-log path, then quits (works with --headless).
/// </summary>
public partial class EmbodimentSpike : Node3D
{
    private const int Size = 257;
    private const float Spacing = 2f;
    private float[] _heights = [];
    private SimRunner? _runner;
    private JobRunner? _jobs;
    private InputLogFile? _log;
    private string _logPath = "";
    private CharacterBody3D _player = null!;
    private MultiMeshInstance3D _puppets = null!;
    private Node3D _bodiesRoot = null!;
    private readonly Dictionary<ulong, (CharacterBody3D Body, NavigationAgent3D Agent)> _bodies = [];
    private long _lastStep = -1;
    private bool _autotest;
    private double _t;
    private bool _finished;
    private Rid _map;

    public override void _Ready()
    {
        _autotest = OS.GetCmdlineUserArgs().Contains("--autotest");
        _player = GetNode<CharacterBody3D>("Player");
        _puppets = GetNode<MultiMeshInstance3D>("Puppets");
        _bodiesRoot = GetNode<Node3D>("Bodies");
        _puppets.Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.7f } };

        _heights = FeudalSim.Sim.World.Heightfield.Generate(42, Size, Spacing);
        var region = GetNode<NavigationRegion3D>("Navigation");
        AddChild(TerrainBuilder.BuildMesh(_heights, Size, Spacing));
        AddChild(TerrainBuilder.BuildCollision(_heights, Size, Spacing));
        var navMesh = new NavigationMesh { CellSize = 0.5f, CellHeight = 0.25f, AgentRadius = 0.5f, AgentHeight = 1.75f, AgentMaxSlope = 40 };
        var source = new NavigationMeshSourceGeometryData3D();
        source.AddFaces(TerrainBuilder.NavigationFaces(_heights, Size, Spacing, extentM: 80), Transform3D.Identity);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        NavigationServer3D.BakeFromSourceGeometryData(navMesh, source);
        region.NavigationMesh = navMesh;
        GD.Print($"Embodiment: navmesh baked over 160 m × 160 m in {sw.ElapsedMilliseconds} ms ({navMesh.GetPolygonCount()} polygons)");
        _map = GetWorld3D().NavigationMap;

        var repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        var content = ContentCompiler.Compile(System.IO.Path.Combine(repo, "content")).Database!;
        var scenario = ScenarioDef.Load(System.IO.Path.Combine(repo, "content", "scenarios", "m0_embodiment.yaml"));
        var dir = System.IO.Path.Combine(repo, "sim_runs", $"embodiment-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
        _logPath = System.IO.Path.Combine(dir, "inputs.fslog");
        _log = InputLogFile.OpenOrCreate(_logPath);
        _jobs = new JobRunner(1);
        _runner = new SimRunner(scenario.CreateWorld(content, _jobs), _log);
        _player.Position = new Vector3(0, Height(0, 10) + 2, 10);
        GD.Print($"Embodiment: {scenario.Id}, {scenario.Settlers} settlers; logging to {_logPath}");
    }

    private float Height(float x, float z) => TerrainBuilder.SampleHeight(_heights, Size, Spacing, x, z);

    public override void _PhysicsProcess(double delta)
    {
        if (_runner is null) { return; }
        _t += delta;
        MovePlayer((float)delta);
        foreach (var (body, agent) in _bodies.Values)
        {
            var v = Vector3.Zero;
            if (!agent.IsNavigationFinished())
            {
                var next = agent.GetNextPathPosition();
                v = (next - body.GlobalPosition) with { Y = 0 };
                v = v.LengthSquared() > 0.0001f ? v.Normalized() * 1.6f : Vector3.Zero;   // canon walk speed
            }

            v.Y = body.IsOnFloor() ? 0 : body.Velocity.Y - (9.8f * (float)delta);
            body.Velocity = v;
            body.MoveAndSlide();
        }
    }

    private void MovePlayer(float delta)
    {
        Vector3 target;
        if (_autotest)
        {
            // 0-5 s stand · 5-30 s out to x=150 · 30-38 s wait (demotion after 5 s) · 38-63 s back · 63-70 s wait
            target = _t < 5 ? new Vector3(0, 0, 10) : _t < 38 ? new Vector3(150, 0, 10) : new Vector3(0, 0, 10);
        }
        else
        {
            var input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            target = _player.Position + new Vector3(input.X, 0, input.Y) * 10;
        }

        var to = (target - _player.Position) with { Y = 0 };
        var v = to.Length() > 0.3f ? to.Normalized() * 6.5f : Vector3.Zero;
        v.Y = _player.IsOnFloor() ? 0 : _player.Velocity.Y - (9.8f * delta);
        _player.Velocity = v;
        _player.MoveAndSlide();
    }

    public override void _Process(double delta)
    {
        if (_runner is null || _finished) { return; }
        var snap = _runner.Snapshots.ReadLatest();
        if (snap.Step != _lastStep && NavigationServer3D.MapGetIterationId(_map) > 0)
        {
            _lastStep = snap.Step;
            Reconcile(snap);
            _runner.Submit(CommandSource.Embodiment, new PlayerMoved(_player.Position.X, _player.Position.Z, _player.Rotation.Y));
            foreach (var (id, (body, _)) in _bodies)
            {
                _runner.Submit(CommandSource.Embodiment, new EmbodimentReport(new EntityId(id), body.GlobalPosition.X, body.GlobalPosition.Z, body.Rotation.Y));
            }
        }

        while (_runner.Events.TryPop(out var e))
        {
            if (e.Payload is LodChanged or Embodied) { GD.Print($"Embodiment: step {e.Step} {e.Payload} · player x {_player.Position.X:F0}"); }
        }

        if (_autotest && _t > 70) { Finish(); }
    }

    private void Reconcile(RenderSnapshot snap)
    {
        var live = new HashSet<ulong>();
        var puppets = 0;
        for (var i = 0; i < snap.Count; i++)
        {
            var id = snap.Ids[i];
            if (snap.Tier[i] == 0)   // LOD0: embodied — Godot owns the body's pose
            {
                live.Add(id);
                if (!_bodies.TryGetValue(id, out var entry))
                {
                    var want = new Vector3(snap.X[i], Height(snap.X[i], snap.Z[i]), snap.Z[i]);
                    var snapped = NavigationServer3D.MapGetClosestPoint(_map, want);
                    var body = new CharacterBody3D { Name = $"Npc{id & 0xFFFF}" };
                    body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.3f, Height = 1.7f }, Position = new Vector3(0, 0.85f, 0) });
                    body.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.7f }, Position = new Vector3(0, 0.85f, 0) });
                    var agent = new NavigationAgent3D { PathDesiredDistance = 0.5f, TargetDesiredDistance = 0.5f, Radius = 0.3f };
                    body.AddChild(agent);
                    _bodiesRoot.AddChild(body);
                    body.GlobalPosition = snapped;
                    entry = (body, agent);
                    _bodies[id] = entry;
                }

                if (snap.HasTarget[i]) { entry.Agent.TargetPosition = new Vector3(snap.TargetX[i], Height(snap.TargetX[i], snap.TargetZ[i]), snap.TargetZ[i]); }
            }
            else
            {
                puppets++;
            }
        }

        foreach (var id in _bodies.Keys.Where(k => !live.Contains(k)).ToList())
        {
            _bodies[id].Body.QueueFree();
            _bodies.Remove(id);
        }

        var mm = _puppets.Multimesh;
        mm.InstanceCount = puppets;
        var k = 0;
        for (var i = 0; i < snap.Count; i++)
        {
            if (snap.Tier[i] == 0) { continue; }
            mm.SetInstanceTransform(k++, new Transform3D(Basis.Identity, new Vector3(snap.X[i], Height(snap.X[i], snap.Z[i]) + 0.85f, snap.Z[i])));
        }
    }

    private void Finish()
    {
        _finished = true;
        _runner!.Pause();
        System.Threading.Thread.Sleep(150);
        var (step, hash) = _runner.Invoke(w => (w.Clock.Step, FeudalSim.Sim.StateHasher.Hash(w))).Result;
        while (_runner.Events.TryPop(out var e)) { if (e.Payload is LodChanged or Embodied) { GD.Print($"Embodiment: step {e.Step} {e.Payload}"); } }
        _runner.Dispose();
        _runner = null;
        _log?.Dispose();
        _log = null;
        _jobs?.Dispose();
        _jobs = null;
        GD.Print($"Embodiment: FINAL step {step} hash {hash:x16}");
        GD.Print($"Embodiment: LOG {_logPath}");
        GetTree().Quit();
    }

    public override void _ExitTree()
    {
        _runner?.Dispose();
        _log?.Dispose();
        _jobs?.Dispose();
        _puppets.Multimesh = null;
    }
}
