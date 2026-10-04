using FeudalSim.Content;
using FeudalSim.Hosting;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// Boots the simulation (M0-11, 20 §12.2): compiles content, builds a scenario, runs it on the SimRunner thread, and
/// draws settlers as capsules from the triple-buffered snapshots. The client never touches sim state directly — only
/// snapshots in, commands out. Default scenario: <c>m1_view</c> (the M1 graybox camp, player at the fire; settlers coloured
/// by activity, overheard talk as subtitles, live AI when <c>OPENROUTER_KEY</c> is set). `-- --scenario &lt;name&gt;` picks
/// another from content/scenarios.
/// Keys: Space pause/resume · 1/2/4/8 time scale · WASD/arrows pan · mouse wheel zoom. `-- --autotest` (M0 smoke scenario)
/// checks movement, pause and time scale against wall-clock time and quits with exit code 0/1 (godot.yml).
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
    private FeudalSim.AI.AiStack? _aiStack;
    private FeudalSim.AI.AiGateway? _gateway;
    private Sim.Content.ContentDatabase? _content;
    private string _scenarioId = "";
    private string _aiStatus = "";
    private Label _subtitles = null!;
    private Camera3D _camera = null!;
    private readonly List<(double At, string Text)> _lines = [];
    private double _clock;
    private readonly Dictionary<ulong, Vector2> _bodies = [];   // LOD0 settlers: the client walks them (ADR-0007)
    private long _lastReportedStep = -1;
    private string? _shotPath;
    private double _shotAt;
    private static readonly Dictionary<string, (Color Color, string Label)> ActivityLook = new()
    {
        ["action.eat_meal"] = (new Color(0.95f, 0.55f, 0.15f), "eating"),
        ["action.drink"] = (new Color(0.25f, 0.55f, 0.95f), "drinking"),
        ["action.sleep"] = (new Color(0.22f, 0.22f, 0.3f), "sleeping"),
        ["action.gather_wood"] = (new Color(0.5f, 0.32f, 0.15f), "gathering wood"),
        ["action.gather_food"] = (new Color(0.3f, 0.75f, 0.3f), "foraging"),
        ["action.tend_fire"] = (new Color(0.9f, 0.15f, 0.1f), "tending fire"),
        ["action.socialize"] = (new Color(0.98f, 0.88f, 0.2f), "talking at the fire"),
        ["action.rest"] = (new Color(0.75f, 0.75f, 0.75f), "resting"),
        ["action.idle"] = (new Color(1f, 1f, 1f), "idle"),
        ["action.flee"] = (new Color(0.6f, 0.2f, 0.8f), "fleeing"),
    };

    public override void _Ready()
    {
        // Keep receiving input and frames while the tree is paused, or Space could never resume.
        ProcessMode = ProcessModeEnum.Always;
        GetTree().AutoAcceptQuit = false;   // closing the window stops the campfire first (see _Notification)
        _autotest = OS.GetCmdlineUserArgs().Contains("--autotest");
        _autotestStartMs = Time.GetTicksMsec();
        _overlay = GetNode<Label>("Overlay/Label");
        _settlers = GetNode<MultiMeshInstance3D>("Settlers");
        _camera = GetNode<Camera3D>("Camera");
        _subtitles = new Label
        {
            Position = new Vector2(12, 520), Size = new Vector2(1100, 200), AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _subtitles.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        _subtitles.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _subtitles.AddThemeConstantOverride("outline_size", 6);
        GetNode<CanvasLayer>("Overlay").AddChild(_subtitles);
        var repo = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
        var compiled = ContentCompiler.Compile(System.IO.Path.Combine(repo, "content"));
        if (!compiled.Ok)
        {
            foreach (var e in compiled.Errors) { GD.PushError($"content/{e}"); }
            _overlay.Text = "Content failed to compile — see the Output panel.";
            return;
        }

        var args = OS.GetCmdlineUserArgs();
        var shot = Array.IndexOf(args, "--shot");   // dev: `-- --shot out.png 20` saves a screenshot after 20 s and quits
        if (shot >= 0 && shot + 2 < args.Length) { (_shotPath, _shotAt) = (args[shot + 1], double.Parse(args[shot + 2], System.Globalization.CultureInfo.InvariantCulture)); }
        var at = Array.IndexOf(args, "--scenario");
        var name = at >= 0 && at + 1 < args.Length ? args[at + 1] : _autotest ? "m0_smoke" : "m1_view";
        var scenario = ScenarioDef.Load(System.IO.Path.Combine(repo, "content", "scenarios", name.EndsWith(".yaml", StringComparison.Ordinal) ? name : name + ".yaml"));
        _content = compiled.Database!;
        _scenarioId = scenario.Id;
        _jobs = new JobRunner(1);
        if (scenario.Player is not null && !_autotest)   // a player can overhear talk: live AI if the key is set, else templates
        {
            var config = FeudalSim.AI.AiConfig.Load(FeudalSim.AI.AiConfig.FindEnvFile(repo));
            _aiStack = FeudalSim.AI.AiStack.Create(config);
            _gateway = _aiStack.CreateGateway();
            _aiStatus = config.TemplateMode ? "AI: template lines (no key)" : $"AI: live · {config.UtilityModel}";
            GD.Print($"SimHost: ai key {(config.ChatKey.IsSet ? "set" : "missing")}, {(config.TemplateMode ? "template" : "live")}");
        }

        _runner = new SimRunner(scenario.CreateWorld(compiled.Database!, _jobs), gateway: _gateway);
        var settlerMaterial = new StandardMaterial3D { VertexColorUseAsAlbedo = true, AlbedoColor = new Color(1, 1, 1) };
        _settlers.MaterialOverride = settlerMaterial;
        _settlers.Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new CapsuleMesh { Radius = 0.3f, Height = 1.7f },
            InstanceCount = 0,
        };
        GD.Print($"SimHost: started {scenario.Id} (seed {scenario.Seed}, {scenario.Settlers} settlers); content {compiled.Database!.Hash:x16}");
        if (scenario.Camp is { } camp) { PlaceMarkers(camp); }
        if (scenario.Player is [var px, var pz]) { AddMarker(new Vector3(px, 0, pz), "you", new Color(0.2f, 0.9f, 0.9f), 0.25f, 2.2f); }
        PlaceTrees();
        StartCampfireAudio(compiled.Database!);
    }

    /// <summary>Graybox markers for the camp's places (M1 camp; the real scene is M1-18).</summary>
    private void PlaceMarkers(CampDef camp)
    {
        var looks = new Dictionary<string, (Color Color, string Label)>
        {
            ["fire"] = (new Color(0.95f, 0.35f, 0.1f), "fire"), ["stores"] = (new Color(0.75f, 0.6f, 0.35f), "food stores"),
            ["shelter"] = (new Color(0.45f, 0.4f, 0.35f), "shelters"), ["water"] = (new Color(0.2f, 0.45f, 0.9f), "stream"),
            ["woods"] = (new Color(0.25f, 0.4f, 0.2f), "woods"), ["forage_ground"] = (new Color(0.4f, 0.65f, 0.3f), "forage ground"),
        };
        foreach (var (key, pos) in camp.Places)
        {
            if (pos.Length != 2 || !looks.TryGetValue(key, out var look)) { continue; }
            AddMarker(new Vector3(pos[0], 0, pos[1]), look.Label, look.Color, key == "fire" ? 0.8f : 2.5f, 0.15f);
        }
    }

    private void AddMarker(Vector3 at, string label, Color color, float radius, float height)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color },
            Position = at + new Vector3(0, height / 2, 0),
        });
        AddChild(new Label3D
        {
            Text = label, Position = at + new Vector3(0, height + 1.2f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64, OutlineSize = 16, PixelSize = 0.04f, Modulate = new Color(1, 1, 1),
        });
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
        Span<int> counts = stackalloc int[_content!.Actions.Count + 1];
        Embody(snapshot, (float)delta);
        for (var i = 0; i < snapshot.Count; i++)
        {
            var basis = new Basis(Vector3.Up, -snapshot.Yaw[i]);
            var at = _bodies.TryGetValue(snapshot.Ids[i], out var body) ? body : new Vector2(snapshot.X[i], snapshot.Z[i]);
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(at.X, 0.85f, at.Y)));
            var action = snapshot.Action[i];
            counts[action < 0 ? counts.Length - 1 : action]++;
            mm.SetInstanceColor(i, action >= 0 && ActivityLook.TryGetValue(_content.Actions[action].Id, out var look) ? look.Color : new Color(0.85f, 0.72f, 0.56f));
        }

        _clock += delta;
        DrainEvents();
        MoveCamera(delta);

        _rateWindow += delta;
        if (_rateWindow >= 1.0)
        {
            var steps = _runner.StepsExecuted;
            _stepsPerSecond = (steps - _rateSteps) / _rateWindow;
            _rateSteps = steps;
            _rateWindow = 0;
        }

        var date = Sim.Time.GameDate.FromGameMs(snapshot.GameMs);
        _overlay.Text = $"FeudalSim · {_scenarioId} · {date} · step {snapshot.Step} · {_stepsPerSecond:F1} steps/s · ×{_timeScale} · {_runner.Mode}\n" +
                        $"{snapshot.Count} settlers · [Space] pause · [1][2][4][8] speed · WASD/arrows pan · wheel zoom" +
                        (_aiStatus.Length > 0 ? $" · {_aiStatus}" : "");
        if (snapshot.CampActive)
        {
            var parts = new List<string>();
            for (var a = 0; a < _content.Actions.Count; a++)
            {
                if (counts[a] > 0) { parts.Add($"{(ActivityLook.TryGetValue(_content.Actions[a].Id, out var look) ? look.Label : _content.Actions[a].Id)} {counts[a]}"); }
            }

            if (counts[^1] > 0) { parts.Add($"deciding {counts[^1]}"); }
            _overlay.Text += $"\nfood {snapshot.Food / 100f:F0} rations · firewood {snapshot.Firewood:F0} · fire {snapshot.FireFuelMin:F0} min left\n{string.Join(" · ", parts)}";
        }

        if (_shotPath is not null && Time.GetTicksMsec() / 1000.0 >= _shotAt)
        {
            GetViewport().GetTexture().GetImage().SavePng(_shotPath);
            GD.Print($"SimHost: screenshot → {_shotPath}");
            _shotPath = null;
            QuitCleanly();
        }

        _lines.RemoveAll(l => _clock - l.At > 14);
        _subtitles.Text = string.Join("\n", _lines.Select(l => l.Text));
        if (_autotest) { Autotest(snapshot); }
    }

    /// <summary>
    /// Settlers near the player are LOD0: the client owns their bodies (ADR-0007). This flat graybox walks each straight to
    /// its sim target at 1.6 m/s (frame delta follows the time scale) and reports the pose every sim step. The full
    /// navmesh/CharacterBody version is the embodiment spike's; M1-18 brings it here.
    /// </summary>
    private void Embody(RenderSnapshot snap, float delta)
    {
        var live = new HashSet<ulong>();
        for (var i = 0; i < snap.Count; i++)
        {
            if (snap.Tier[i] != 0) { continue; }
            var id = snap.Ids[i];
            live.Add(id);
            var pos = _bodies.TryGetValue(id, out var p) ? p : new Vector2(snap.X[i], snap.Z[i]);
            if (snap.HasTarget[i])
            {
                var to = new Vector2(snap.TargetX[i], snap.TargetZ[i]) - pos;
                var step = 1.6f * delta;
                pos = to.Length() <= step ? pos + to : pos + (to.Normalized() * step);
            }

            _bodies[id] = pos;
        }

        foreach (var id in _bodies.Keys.Where(k => !live.Contains(k)).ToList()) { _bodies.Remove(id); }
        if (snap.Step == _lastReportedStep || _runner!.Mode == RunMode.Paused) { return; }
        _lastReportedStep = snap.Step;
        foreach (var (id, pos) in _bodies) { _runner.Submit(Sim.Commands.CommandSource.Embodiment, new Sim.Commands.EmbodimentReport(new Sim.Core.EntityId(id), pos.X, pos.Y, 0f)); }
    }

    /// <summary>Overheard talk (22 §9.2): rendered lines (JSON) or the template subtitle, shown for 14 s, newest last.</summary>
    private void DrainEvents()
    {
        while (_runner!.Events.TryPop(out var e))
        {
            if (e.Payload is not Sim.Events.AiResultApplied r || string.IsNullOrWhiteSpace(r.Text)) { continue; }
            if (r.UsedFallback || !r.Text.TrimStart().StartsWith('['))
            {
                _lines.Add((_clock, $"({r.Text})"));
            }
            else
            {
                try
                {
                    foreach (var node in System.Text.Json.Nodes.JsonNode.Parse(r.Text)!.AsArray())
                    {
                        _lines.Add((_clock, $"{(string?)node?["speaker"]}: “{(string?)node?["line"]}”"));
                    }
                }
                catch (System.Text.Json.JsonException) { _lines.Add((_clock, r.Text)); }
            }
        }

        while (_lines.Count > 8) { _lines.RemoveAt(0); }
    }

    private void MoveCamera(double delta)
    {
        if (_autotest) { return; }
        var pan = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) { pan.Z -= 1; }
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) { pan.Z += 1; }
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) { pan.X -= 1; }
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) { pan.X += 1; }
        // Real (unscaled) seconds, so panning speed doesn't change with the time scale.
        _camera.Position += pan * (float)(delta / Math.Max(Engine.TimeScale, 0.01)) * (10f + (_camera.Position.Y * 0.8f));
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
        if (@event is InputEventMouseButton { Pressed: true } wheel && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            var forward = -_camera.GlobalTransform.Basis.Z;
            var step = wheel.ButtonIndex == MouseButton.WheelUp ? 3f : -3f;
            var next = _camera.Position + (forward * step);
            if (next.Y is > 4f and < 120f) { _camera.Position = next; }
            return;
        }

        if (_runner is null || @event is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        switch (key.Keycode)
        {
            case Key.Space: TogglePause(); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key4: SetSpeed(4); break;
            case Key.Key8: SetSpeed(8); break;
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

    /// <summary>
    /// Window close: stop the looping campfire, give the audio server a moment to free its playback (it does so on its own
    /// thread), then quit — otherwise Godot reports the stream as leaked at exit.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) { QuitCleanly(); }
    }

    private void QuitCleanly()
    {
        _campfire?.Stop();
        GetTree().CreateTimer(0.25, processAlways: true, ignoreTimeScale: true).Timeout += () => GetTree().Quit();
    }

    public override void _ExitTree()
    {
        _runner?.Dispose();
        _gateway?.Dispose();
        _aiStack?.Dispose();
        _jobs?.Dispose();
        _settlers.Multimesh = null;   // release the MultiMesh/CapsuleMesh before engine shutdown
        _settlers.MaterialOverride = null;
        if (_campfire is not null)
        {
            _campfire.Stop();   // a live playback keeps the stream referenced past shutdown
            _campfire.Stream?.Dispose();
            _campfire.Stream = null;
        }
    }
}
