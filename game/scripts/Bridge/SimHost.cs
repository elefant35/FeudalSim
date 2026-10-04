using FeudalSim.Content;
using FeudalSim.Hosting;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// Boots the simulation (M0-11, 20 §12.2): compiles content, builds the smoke scenario, runs it on the
/// SimRunner thread, and draws settlers as capsules from the triple-buffered snapshots. The client
/// never touches sim state directly — only snapshots in, commands out.
/// Keys: Space pause/resume · 1/2/4 time scale. `-- --autotest` checks movement, pause and time scale against
/// wall-clock time, prints the results and quits with exit code 0/1 (works with --headless; used by godot.yml).
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
    private bool _autotest;
    private ulong _autotestStartMs;
    private int _autotestPhase;
    private long _autotestMark;
    private float[] _autotestPositions = [];
    private readonly List<string> _autotestResults = [];
    private bool _autotestFailed;
    private AudioStreamPlayer3D? _campfire;

    public override void _Ready()
    {
        // Keep receiving input and frames while the tree is paused, or Space could never resume.
        ProcessMode = ProcessModeEnum.Always;
        _autotest = OS.GetCmdlineUserArgs().Contains("--autotest");
        _autotestStartMs = Time.GetTicksMsec();
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
        PlaceTrees();
        StartCampfireAudio(compiled.Database!);
    }

    /// <summary>M0-A4: the generated pine (art pipeline → .glb → Godot import) placed around the camp.</summary>
    private void PlaceTrees()
    {
        var pine = GD.Load<PackedScene>("res://assets/flora/pine_a.glb");
        for (var i = 0; i < 14; i++)
        {
            var angle = i * 0.449f + 0.3f;   // deterministic ring, no randomness in the client
            var radius = 26f + (i % 4) * 5f;
            var tree = pine.Instantiate<Node3D>();
            tree.Position = new Vector3(10 + Mathf.Cos(angle) * radius, 0, -6 + Mathf.Sin(angle) * radius);
            tree.RotationDegrees = new Vector3(0, i * 37, 0);
            tree.Scale = Vector3.One * (0.9f + (i % 3) * 0.15f);
            AddChild(tree);
        }

        GD.Print("SimHost: placed 14 × res://assets/flora/pine_a.glb");
    }

    /// <summary>M0-AU3: play a sound through its content mapping (content/audio/events.yaml → AudioStreamPlayer3D).</summary>
    private void StartCampfireAudio(Sim.Content.ContentDatabase content)
    {
        var mapping = content.Audio.FirstOrDefault(a => a.Id == "audio.world.campfire");
        if (mapping is null) { GD.PushWarning("audio.world.campfire is not mapped"); return; }
        var stream = GD.Load<AudioStreamWav>("res://" + mapping.Files[0]["game/".Length..]);
        if (mapping.Loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopEnd = (int)(stream.GetLength() * stream.MixRate);
        }

        _campfire = new AudioStreamPlayer3D { Stream = stream, Position = new Vector3(10, 0.5f, -6), UnitSize = 6, Autoplay = true };
        AddChild(_campfire);
        GD.Print($"SimHost: audio {mapping.Id} → {mapping.Files[0]} (bus {mapping.Bus}, loop {mapping.Loop}, spatial {mapping.Spatial}), playing {_campfire.Playing || _campfire.Autoplay}");
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
        if (_autotest) { Autotest(snapshot); }
    }

    /// <summary>
    /// M0 exit criterion "a capsule moving by sim commands; pause and time scale work", timed by the wall clock
    /// (Engine.TimeScale scales frame delta): ×1 for 3 s → pause 2 s → ×4 for 3 s, through the same paths as the keys.
    /// </summary>
    private void Autotest(RenderSnapshot snap)
    {
        var t = (Time.GetTicksMsec() - _autotestStartMs) / 1000.0;
        switch (_autotestPhase)
        {
            case 0 when t >= 1.0:
                (_autotestMark, _autotestPositions, _autotestPhase) = (snap.Step, Positions(snap), 1);
                break;
            case 1 when t >= 4.0:
                Expect("×1 rate", (snap.Step - _autotestMark) / 3.0, 10, 0.1);
                Expect("settlers moved at ×1", Moved(snap), 3, null);
                TogglePause();
                _autotestPhase = 2;
                break;
            case 2 when t >= 4.5:   // let an in-flight step land, then watch for 2 s
                (_autotestMark, _autotestPositions, _autotestPhase) = (snap.Step, Positions(snap), 3);
                break;
            case 3 when t >= 6.5:
                Expect("steps while paused", snap.Step - _autotestMark, 0, 0);
                Expect("settlers moved while paused", Moved(snap), 0, 0);
                Expect("tree paused", GetTree().Paused ? 1 : 0, 1, 0);
                TogglePause();
                SetSpeed(4);
                _autotestPhase = 4;
                break;
            case 4 when t >= 7.5:
                (_autotestMark, _autotestPhase) = (snap.Step, 5);
                break;
            case 5 when t >= 10.5:
                Expect("×4 rate", (snap.Step - _autotestMark) / 3.0, 40, 0.1);
                Expect("Engine.TimeScale", Engine.TimeScale, 4, 0);
                SetSpeed(1);
                foreach (var line in _autotestResults) { GD.Print(line); }
                GD.Print($"SimHost: AUTOTEST {(_autotestFailed ? "FAIL" : "PASS")} · dilation events {_runner!.TimeDilationEvents}");
                _campfire?.Stop();   // the audio server frees the playback on its own thread; give it a moment
                (_autotestMark, _autotestPhase) = ((long)(t * 1000), 6);
                break;
            case 6 when t * 1000 >= _autotestMark + 250:
                _autotestPhase = 7;
                GetTree().Quit(_autotestFailed ? 1 : 0);
                break;
        }
    }

    private static float[] Positions(RenderSnapshot snap)
    {
        var p = new float[snap.Count * 2];
        for (var i = 0; i < snap.Count; i++) { (p[2 * i], p[(2 * i) + 1]) = (snap.X[i], snap.Z[i]); }
        return p;
    }

    private int Moved(RenderSnapshot snap)
    {
        var moved = 0;
        for (var i = 0; i < snap.Count && (2 * i) + 1 < _autotestPositions.Length; i++)
        {
            var dx = snap.X[i] - _autotestPositions[2 * i];
            var dz = snap.Z[i] - _autotestPositions[(2 * i) + 1];
            if ((dx * dx) + (dz * dz) > 0.01f) { moved++; }
        }

        return moved;
    }

    /// <summary>Records a check: within ±relTol of expected, or (relTol null) at least expected.</summary>
    private void Expect(string name, double actual, double expected, double? relTol)
    {
        var ok = relTol is { } tol ? Math.Abs(actual - expected) <= Math.Max(tol * expected, 1e-9) : actual >= expected;
        _autotestFailed |= !ok;
        _autotestResults.Add($"SimHost: autotest {(ok ? "ok  " : "FAIL")} {name}: {actual:0.##} (expected {(relTol is null ? "≥ " : "")}{expected}{(relTol is > 0 ? $" ± {relTol:P0}" : "")})");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_runner is null || @event is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        switch (key.Keycode)
        {
            case Key.Space: TogglePause(); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key4: SetSpeed(4); break;
        }
    }

    private void TogglePause()
    {
        if (_runner!.Mode == RunMode.Paused) { _runner.Resume(); } else { _runner.Pause(); }
        GetTree().Paused = _runner.Mode == RunMode.Paused;
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
        _settlers.Multimesh = null;   // release the MultiMesh/CapsuleMesh before engine shutdown
        if (_campfire is not null)
        {
            _campfire.Stop();   // a live playback keeps the stream referenced past shutdown
            _campfire.Stream?.Dispose();
            _campfire.Stream = null;
        }
    }
}
