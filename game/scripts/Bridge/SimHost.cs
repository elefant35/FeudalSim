using FeudalSim.Content;
using FeudalSim.Hosting;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// Boots the simulation (M0-11, 20 §12.2): compiles content, builds the smoke scenario, runs it on the
/// SimRunner thread, and draws settlers as capsules from the triple-buffered snapshots. The client
/// never touches sim state directly — only snapshots in, commands out.
/// Keys: Space pause/resume · 1/2/4 time scale.
/// </summary>
public partial class SimHost : Node3D
{
    private SimRunner? _runner;
    private JobRunner? _jobs;
    private MultiMeshInstance3D _settlers = null!;
    private Label _overlay = null!;
    private double _rateWindow;
    private long _rateSteps;
    private double _stepsPerSecond;
    private double _timeScale = 1;

    public override void _Ready()
    {
        _overlay = GetNode<Label>("Overlay/Label");
        _settlers = GetNode<MultiMeshInstance3D>("Settlers");
        var repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        var compiled = ContentCompiler.Compile(System.IO.Path.Combine(repo, "content"));
        if (!compiled.Ok)
        {
            foreach (var e in compiled.Errors) { GD.PushError($"content/{e}"); }
            _overlay.Text = "Content failed to compile — see the Output panel.";
            return;
        }

        var scenario = ScenarioDef.Load(System.IO.Path.Combine(repo, "content", "scenarios", "m0_smoke.yaml"));
        _jobs = new JobRunner(1);
        _runner = new SimRunner(scenario.CreateWorld(compiled.Database!, _jobs));
        _settlers.Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.7f },
            InstanceCount = 0,
        };
        GD.Print($"SimHost: started {scenario.Id} (seed {scenario.Seed}, {scenario.Settlers} settlers); content {compiled.Database!.Hash:x16}");
    }

    public override void _Process(double delta)
    {
        if (_runner is null) { return; }
        var snapshot = _runner.Snapshots.ReadLatest();
        var mm = _settlers.Multimesh;
        if (mm.InstanceCount != snapshot.Count) { mm.InstanceCount = snapshot.Count; }
        for (var i = 0; i < snapshot.Count; i++)
        {
            var basis = new Basis(Vector3.Up, -snapshot.Yaw[i]);
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(snapshot.X[i], 0.85f, snapshot.Z[i])));
        }

        _rateWindow += delta;
        if (_rateWindow >= 1.0)
        {
            var steps = _runner.StepsExecuted;
            _stepsPerSecond = (steps - _rateSteps) / _rateWindow;
            _rateSteps = steps;
            _rateWindow = 0;
        }

        var date = Sim.Time.GameDate.FromGameMs(snapshot.GameMs);
        _overlay.Text = $"FeudalSim M0 · {date} · step {snapshot.Step} · {_stepsPerSecond:F1} steps/s · ×{_timeScale} · {_runner.Mode}\n" +
                        $"{snapshot.Count} settlers · dilation {_runner.TimeDilationEvents} · [Space] pause · [1][2][4] speed";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_runner is null || @event is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        switch (key.Keycode)
        {
            case Key.Space:
                if (_runner.Mode == RunMode.Paused) { _runner.Resume(); } else { _runner.Pause(); }
                GetTree().Paused = _runner.Mode == RunMode.Paused;
                break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key4: SetSpeed(4); break;
        }
    }

    private void SetSpeed(double scale)
    {
        _timeScale = scale;
        _runner!.SetTimeScale(scale);
        Engine.TimeScale = scale;   // Godot bodies follow sim time (20 §5.3)
    }

    public override void _ExitTree()
    {
        _runner?.Dispose();
        _jobs?.Dispose();
    }
}
