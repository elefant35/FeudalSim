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
    private DirectionalLight3D? _sun;

    /// <summary>10 §6.1: the sun crosses the sky between sunrise and sunset; cloud dims it and night leaves moonlight.</summary>
    private void UpdateSun(in Sim.Climate.WeatherState weather, long minute)
    {
        if (_sun is null) { return; }
        var day = Sim.Climate.Weather.DayOfYear(minute);
        var length = Sim.Climate.Weather.DaylightHours(day);
        var hour = minute % 1440 / 60f;
        var t = (hour - (12f - (length / 2f))) / length;                 // 0 at sunrise, 1 at sunset
        var up = t is > 0f and < 1f;
        var elevation = up ? Mathf.Sin(Mathf.Pi * t) * 60f : 20f;          // the moon stands in at night
        _sun.RotationDegrees = new Vector3(-Mathf.Max(5f, elevation), up ? -90f + (180f * t) : 35f, 0);
        var cloud = weather.Sky switch { Sim.Climate.Sky.Clear => 1f, Sim.Climate.Sky.Cloudy => 0.6f, Sim.Climate.Sky.Fog => 0.45f, Sim.Climate.Sky.Drizzle => 0.5f, Sim.Climate.Sky.Rain => 0.4f, _ => 0.3f };
        var moon = 0.04f + (0.10f * (1f - Mathf.Abs((Sim.Climate.Weather.MoonPhase(minute) * 2f) - 1f)));
        _sun.LightEnergy = up ? Mathf.Lerp(0.15f, 1.1f, Mathf.Sin(Mathf.Pi * t)) * cloud : moon * cloud;
        _sun.LightColor = up && (t < 0.12f || t > 0.88f) ? new Color(1f, 0.75f, 0.55f) : up ? new Color(1f, 0.97f, 0.92f) : new Color(0.6f, 0.7f, 1f);
        _sun.ShadowEnabled = up || moon > 0.1f;
    }
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
        ["action.converse"] = (new Color(0.4f, 1f, 1f), "talking with you"),
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
        if (args.Contains("--facing-check") && CharacterKit.Load() is { } kit)   // dev: which way the body faces vs which way its clips walk
        {
            GD.Print("SimHost: facing\n" + kit.FacingReport(this));
            GetTree().Quit();
            return;
        }

        var shot = Array.IndexOf(args, "--shot");   // dev: `-- --shot out.png 20` saves a screenshot after 20 s and quits
        if (shot >= 0 && shot + 2 < args.Length) { (_shotPath, _shotAt) = (args[shot + 1], double.Parse(args[shot + 2], System.Globalization.CultureInfo.InvariantCulture)); }
        var fps = Array.IndexOf(args, "--fps");   // dev: `-- --fps 60` logs frame-time percentiles over 60 s (after 3 s warm-up) and quits
        if (fps >= 0 && fps + 1 < args.Length) { _fpsSeconds = double.Parse(args[fps + 1], System.Globalization.CultureInfo.InvariantCulture); }
        var at = Array.IndexOf(args, "--scenario");
        var flatCampTest = args.Contains("--autotest-camp") || args.Contains("--autotest-dialogue") || args.Contains("--autotest-knap");
        var name = at >= 0 && at + 1 < args.Length ? args[at + 1] : _autotest ? "m0_smoke" : flatCampTest ? "m1_view" : args.Contains("--autotest-island") ? "m2_landfall" : "m2_landfall";   // M2-FP1: the island by default; the M1 autotests keep the flat camp
        var scenario = ScenarioDef.Load(System.IO.Path.Combine(repo, "content", "scenarios", name.EndsWith(".yaml", StringComparison.Ordinal) ? name : name + ".yaml"));
        _content = compiled.Database!;
        _scenarioId = scenario.Id;
        _jobs = new JobRunner(1);
        _autotestDialogue = args.Contains("--autotest-dialogue");
        _autotestKnap = args.Contains("--autotest-knap");
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("FEUDALSIM_WORLD_CACHE")))   // M2-02: generated regions cache under user://worlds
        {
            System.Environment.SetEnvironmentVariable("FEUDALSIM_WORLD_CACHE", ProjectSettings.GlobalizePath("user://worlds"));
        }
        _openKnap = args.Contains("--open-knap");
        _autotestCamp = _autotestDialogue || args.Contains("--autotest-camp");
        _autotestIsland = args.Contains("--autotest-island");
        var templateOnly = _autotestCamp || _autotestKnap || _autotestIsland || _shotPath is not null || args.Contains("--template");   // autotests, screenshots and `--template` never call a model (unless --live)
        FeudalSim.AI.AiConfig? config = null;
        if (scenario.Player is not null && !_autotest)   // a player can overhear talk: live AI if the key is set, else templates
        {
            config = templateOnly && !args.Contains("--live") ? new FeudalSim.AI.AiConfig { LlmMode = "template" }   // the camp autotest never calls a model (unless --live)
                : FeudalSim.AI.AiConfig.Load(FeudalSim.AI.AiConfig.FindEnvFile(repo));
            _aiStack = FeudalSim.AI.AiStack.Create(config);
            _gateway = _aiStack.CreateGateway();
            _aiStatus = config.TemplateMode ? "AI: template lines (no key)" : $"AI: live · {config.UtilityModel}";
            GD.Print($"SimHost: ai key {(config.ChatKey.IsSet ? "set" : "missing")}, {(config.TemplateMode ? "template" : "live")}");
        }

        if (scenario.World is { } region)   // M2-FP1: generate (or load) the region on all cores before the sim's single-thread runner gets it
        {
            using var gen = new JobRunner(Math.Max(1, System.Environment.ProcessorCount - 1));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            WorldCache.GetOrGenerate(compiled.Database!, region.Spec, region.Seed ?? scenario.Seed, jobs: gen);
            GD.Print($"SimHost: world map ready in {sw.Elapsed.TotalMilliseconds:F0} ms");
        }

        var world = scenario.CreateWorld(compiled.Database!, _jobs);
        var anchor = scenario.Anchor(world.Map);
        if (world.Map is { } map)
        {
            using var gen = new JobRunner(Math.Max(1, System.Environment.ProcessorCount - 1));
            _island = World.Island.Build(this, map, _camera, gen);
            World.Island.Atmosphere(GetNode<WorldEnvironment>("WorldEnvironment"));
            InitNodes();
            GetNode<Node3D>("Ground").Visible = false;
            if (anchor is not null) { _camera.Position = new Vector3(anchor.FireX, Ground(anchor.FireX, anchor.FireZ) + 34, anchor.FireZ + 26); }
            if (anchor is not null && map.Landing is { } landing) { _islandFacing = Mathf.Atan2(-(landing.WreckX - anchor.FireX), -(landing.WreckZ - anchor.FireZ)); }   // FP2: you start looking out at the wreck
        }

        _campRecord = world.Camp;
        _runner = new SimRunner(world, gateway: _gateway);
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
        var dressed = _island is not null && scenario.Camp is not null && !args.Contains("--view") ? World.CampDressing.Build(this, _island, _campRecord, scenario.Settlers) : 0;
        if (dressed > 0) { GD.Print($"SimHost: camp dressed with {dressed} models"); }
        else if (scenario.Camp is not null) { PlaceMarkers(_campRecord); }   // markers for the flat camp and the overhead view
        if (scenario.PlayerStart(anchor) is [var px, var pz])
        {
            if (!_autotest && !args.Contains("--view")) { InitPlay(new Vector2(px, pz), config, compiled.Database!); }
            else { AddMarker(new Vector3(px, Ground(px, pz), pz), "you", new Color(0.2f, 0.9f, 0.9f), 0.25f, 2.2f); }
        }

        if (_island is null) { PlaceTrees(); }   // the flat camp's ring of pines; the island's trees are its nodes (M2-FP3)
        StartCampfireAudio(compiled.Database!);
    }

    private World.Island? _island;
    private Sim.World.CampRecord _campRecord;
    private bool _autotestIsland;

    /// <summary>
    /// `--autotest-island` (M2-FP1): Terrain3D draws the sim's 2 m heights (64 points over the map and 64 around the camp
    /// within 1 cm), the camp's places and the player stand on dry land, every settler has a body standing on the ground,
    /// and the wreck lies offshore. Exit 0/1.
    /// </summary>
    private void IslandAutotest()
    {
        if (_autotestPhase != 0) { IslandWork(); return; }
        _autotestPhase = 1;
        var island = _island;
        Expect("island built", island is null ? 0 : 1, 1, 0);
        if (island is not null)
        {
            var g = island.Map.Grid;
            var half = (g.Size - 1) * g.CellM / 2f;
            float maxErr = 0f, campErr = 0f;
            for (var i = 0; i < 64; i++)
            {
                float x = ((i * 7919) % 4000 * 2f) - half + 1f, z = ((i * 104729) % 4000 * 2f) - half + 1f;   // on vertices, away from the far edge
                if (island.Terrain is { } t) { maxErr = Math.Max(maxErr, Math.Abs(t.HeightAt(new Vector3(x, 0, z)) - island.HeightAt(x, z))); }
                float cx = _campRecord.FireX + (((i * 37) % 64) - 32) * 16f, cz = _campRecord.FireZ + (((i * 53) % 64) - 32) * 16f;
                cx = MathF.Round(cx / 2f) * 2f;
                cz = MathF.Round(cz / 2f) * 2f;
                if (island.Terrain is { } t2) { campErr = Math.Max(campErr, Math.Abs(t2.HeightAt(new Vector3(cx, 0, cz)) - island.HeightAt(cx, cz))); }
            }

            Expect($"Terrain3D matches the sim heights over the map (max error {maxErr:F3} m ≤ 0.01)", maxErr <= 0.01f ? 1 : 0, 1, 0);
            Expect($"Terrain3D matches the sim heights around the camp (max error {campErr:F3} m ≤ 0.01)", campErr <= 0.01f ? 1 : 0, 1, 0);
            bool Dry(float x, float z) => Hosting.CampAnchor.Cell(g, x, z) is var c and >= 0 && g.Land[c] == 1 && island.HeightAt(x, z) > 0f;
            var dryPlaces = 0;
            foreach (var kind in new[] { Sim.Content.PlaceKind.Fire, Sim.Content.PlaceKind.Stores, Sim.Content.PlaceKind.Shelter, Sim.Content.PlaceKind.Water, Sim.Content.PlaceKind.Woods, Sim.Content.PlaceKind.ForageGround })
            {
                var (x, z) = _campRecord.Place(kind);
                if (Dry(x, z)) { dryPlaces++; }
            }

            Expect("camp places on dry land", dryPlaces, 6, 0);
            Expect("player on dry land", Dry(_player.X, _player.Y) ? 1 : 0, 1, 0);
            if (island.Map.Landing is { } l) { Expect("wreck offshore (ground below sea level)", island.HeightAt(l.WreckX, l.WreckZ) < 0f ? 1 : 0, 1, 0); }
            var standing = _people.Values.Count(p => Math.Abs(p.Body.Position.Y - Ground(p.Body.Position.X, p.Body.Position.Z)) < 0.2f);
            Expect("settlers with bodies on the ground", standing, 24, 0);
            _walkSamples = _walkReversed = 0;   // counted from here while the settlers go about their work
            GD.Print("SimHost: " + island.Report);
        }

        Expect($"nodes drawn around the camp ({_nodes?.Instances ?? 0}; {_nodes?.ArtTypes ?? 0} types with art)", _nodes?.Instances ?? 0, 50, null);
        SetSpeed(8);   // the work below at ×8
        _islandMark = _clock;
    }

    private double _islandMark;
    private Vector2? _autotestWalkTo;
    private float _autotestLookY = 1.4f;   // how high above the ground the autotest looks (a trunk, or a plant at 0.2 m)
    private (int Chunk, int Index)? _islandTarget;
    private int _foraged, _felled, _eaten;

    /// <summary>
    /// FP3: walk to the nearest standing tree, look at it, [E] fells it (the stages work themselves), then the nearest
    /// in-season plant, [E] gathers it. The look-at prompt must name the verb before [E] is pressed.
    /// </summary>
    private void IslandWork()
    {
        var t = _clock - _islandMark;
        if ((int)(t / 10) != (int)((t - GetProcessDeltaTime()) / 10) && _autotestWalkTo is { } goal) { GD.Print($"SimHost: island autotest phase {_autotestPhase} t {t:F1} player {_player} goal {goal} d {goal.DistanceTo(_player):F1} look {_look?.Kind}:{_look?.Name} near {_nodes?.Near.Count}"); }
        var season = Sim.Time.GameDate.FromGameMs(_runner!.Snapshots.ReadLatest().GameMs).Season.ToString().ToLowerInvariant();
        switch (_autotestPhase)
        {
            case 1:
                _islandTarget = _nodes?.Nearest(_player.X, _player.Y, (d, n, s) => d.Kind == Sim.Content.NodeKind.Tree && n.Size >= 1 && s == 0) is { } tree ? (tree.Chunk, tree.Index) : null;
                var at = _islandTarget is null ? (Vector3?)null : _nodes!.Nearest(_player.X, _player.Y, (d, n, s) => d.Kind == Sim.Content.NodeKind.Tree && n.Size >= 1 && s == 0)!.Value.At;
                Expect("a standing tree on the loaded island", at is null ? 0 : 1, 1, 0);
                if (at is not { } a) { _autotestPhase = 9; break; }
                _autotestWalkTo = new Vector2(a.X, a.Z);
                (_autotestPhase, _islandMark) = (2, _clock);
                break;
            case 2 when _look is { } lk && lk.Kind == "tree" && LookPromptFor(lk).StartsWith("[E] fell", StringComparison.Ordinal):
                Expect($"looking at a tree offers to fell it ({LookPromptFor(lk)})", 1, 1, 0);
                Interact();
                (_autotestPhase, _islandMark) = (3, _clock);
                break;
            case 2 when t > 120:
                Expect("reached and looked at the tree", 0, 1, 0);
                _autotestPhase = 9;
                break;
            case 3 when _felled > 0:
                Expect("felled the tree (rough log)", 1, 1, 0);
                (_autotestPhase, _islandMark) = (4, _clock);
                break;
            case 3 when t > 300:
                Expect("felled the tree in time", 0, 1, 0);
                _autotestPhase = 9;
                break;
            case 4 when t > 2:
                Expect("the tree shows as a stump", _islandTarget is { } tt && _nodes!.Nearest(_player.X, _player.Y, (d, n, s) => s == World.NodeDressing.Felled) is { } stump && (stump.Chunk, stump.Index) == tt ? 1 : 0, 1, 0);
                var plant = _nodes!.Nearest(_player.X, _player.Y, (d, n, s) => d.Forage is { } f && _content!.Items[_content.ItemHandle(f.Item)].Food is not null && s == 0 && (d.Seasons.Count == 0 || d.Seasons.Contains(season)));
                Expect($"an in-season food plant to gather ({season})", plant is null ? 0 : 1, 1, 0);
                if (plant is not { } p) { _autotestPhase = 9; break; }
                (_autotestWalkTo, _autotestLookY) = (new Vector2(p.At.X, p.At.Z), 0.2f);
                (_autotestPhase, _islandMark) = (5, _clock);
                break;
            case 5 when _look is { } lk2 && LookPromptFor(lk2).StartsWith("[E] gather", StringComparison.Ordinal):
                Expect($"looking at a plant offers to gather it ({LookPromptFor(lk2)})", 1, 1, 0);
                Interact();
                (_autotestPhase, _islandMark) = (6, _clock);
                break;
            case 5 when t > 120:
                Expect("reached and looked at the plant", 0, 1, 0);
                _autotestPhase = 9;
                break;
            case 6 when _foraged > 0 || t > 10:
                Expect("gathered it", _foraged, 1, null);
                if (_foraged == 0) { _autotestPhase = 9; break; }
                ToggleInventory();
                (_autotestPhase, _islandMark) = (7, _clock);
                break;
            case 7 when InventoryOpen && _invItems.Any(i => i.Sat >= 0):
                _invSelected = _invItems.FindIndex(i => i.Sat >= 0);
                Expect($"[I] lists it as food ({_invItems[_invSelected].Name}, {_invItems[_invSelected].Sat:0} Sat)", 1, 1, 0);
                InventoryKey(Key.Enter);
                (_autotestPhase, _islandMark) = (8, _clock);
                break;
            case 7 when t > 10:
                Expect("[I] lists the gathered food", 0, 1, 0);
                _autotestPhase = 9;
                break;
            case 8 when _eaten > 0 || t > 10:
                Expect("ate it ([Enter] → Eat → Ate)", _eaten, 1, null);
                if (InventoryOpen) { ToggleInventory(); }
                (_autotestWalkTo, _autotestLookY) = (new Vector2(_campRecord.WaterX, _campRecord.WaterZ), 8f);   // to the brook, looking up (no target)
                (_autotestPhase, _islandMark) = (20, _clock);
                break;
            case 20 when _waterHere is not null && _look is null && new Vector2(_campRecord.WaterX, _campRecord.WaterZ).DistanceTo(_player) < 2.5f:
                Expect($"by the brook the prompt offers a drink (from {_waterHere})", 1, 1, 0);
                Interact();
                (_autotestPhase, _islandMark) = (21, _clock);
                break;
            case 20 when t > 150:
                Expect("reached the brook", 0, 1, 0);
                _autotestPhase = 9;
                break;
            case 21 when _drank > 0 || t > 10:
                Expect("drank ([E] → Drink → Drank)", _drank, 1, null);
                _autotestPhase = 9;
                break;
            case 9:
                Expect($"walking settlers face where they go ({_walkReversed} of {_walkSamples} frames backwards)", _walkSamples > 50 && _walkReversed == 0 ? 1 : 0, 1, 0);
                _autotestWalkTo = null;
                foreach (var line in _autotestResults) { GD.Print(line); }
                GD.Print($"SimHost: ISLAND AUTOTEST {(_autotestFailed ? "FAIL" : "PASS")}");
                _campfire?.Stop();
                _autotestPhase = 10;
                GetTree().Quit(_autotestFailed ? 1 : 0);
                break;
        }
    }

    /// <summary>Ground height for anything standing in the world (0 on the flat camp; the island's 2 m field, or the sea surface).</summary>
    private float Ground(float x, float z) => _island?.StandAt(x, z) ?? 0f;

    /// <summary>Graybox markers for the camp's places, where the sim put them (anchored on the island; M1 camp otherwise).</summary>
    private void PlaceMarkers(in Sim.World.CampRecord camp)
    {
        var looks = new Dictionary<string, (Color Color, string Label)>
        {
            ["fire"] = (new Color(0.95f, 0.35f, 0.1f), "fire"), ["stores"] = (new Color(0.75f, 0.6f, 0.35f), "food stores"),
            ["shelter"] = (new Color(0.45f, 0.4f, 0.35f), "shelters"), ["water"] = (new Color(0.2f, 0.45f, 0.9f), "stream"),
            ["woods"] = (new Color(0.25f, 0.4f, 0.2f), "woods"), ["forage_ground"] = (new Color(0.4f, 0.65f, 0.3f), "forage ground"),
        };
        foreach (var (key, kind) in new[] { ("fire", Sim.Content.PlaceKind.Fire), ("stores", Sim.Content.PlaceKind.Stores), ("shelter", Sim.Content.PlaceKind.Shelter), ("water", Sim.Content.PlaceKind.Water), ("woods", Sim.Content.PlaceKind.Woods), ("forage_ground", Sim.Content.PlaceKind.ForageGround) })
        {
            var look = looks[key];
            var (x, z) = camp.Place(kind);
            AddMarker(new Vector3(x, Ground(x, z), z), look.Label, look.Color, key == "fire" ? 0.8f : 2.5f, 0.15f);
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

        _campfire = new AudioStreamPlayer3D { Stream = stream, Position = new Vector3(_campRecord.FireX, Ground(_campRecord.FireX, _campRecord.FireZ) + 0.5f, _campRecord.FireZ), UnitSize = 6, Autoplay = true };
        AddChild(_campfire);
        GD.Print($"SimHost: audio {mapping.Id} → {mapping.Files[0]} (bus {mapping.Bus}, loop {mapping.Loop}, spatial {mapping.Spatial}), playing {_campfire.Playing || _campfire.Autoplay}");
    }

    private double _fpsSeconds, _fpsElapsed;
    private readonly List<double> _frameMs = new(8192);

    /// <summary>`--fps N`: frame times after a 3 s warm-up, then p50/p95/p99/max and the share of frames slower than 60 fps.</summary>
    private void SampleFrame(double delta)
    {
        _fpsElapsed += delta;
        if (_fpsElapsed < 3.0) { return; }
        _frameMs.Add(delta * 1000.0);
        if (_fpsElapsed < 3.0 + _fpsSeconds) { return; }
        _frameMs.Sort();
        double Q(double q) => _frameMs[Math.Min(_frameMs.Count - 1, (int)Math.Ceiling(q * _frameMs.Count) - 1)];
        var slow = _frameMs.Count(f => f > 1000.0 / 55.0) / (double)_frameMs.Count;
        GD.Print(FormattableString.Invariant($"fps: {_frameMs.Count} frames in {_fpsSeconds:F0} s · mean {_frameMs.Count / _fpsSeconds:F1} fps · frame ms p50 {Q(0.5):F2} p95 {Q(0.95):F2} p99 {Q(0.99):F2} max {_frameMs[^1]:F1} · slower than 55 fps {slow:P2}"));
        _fpsSeconds = 0;
        GetTree().Quit(0);
    }

    public override void _Process(double delta)
    {
        if (_fpsSeconds > 0) { SampleFrame(delta); }
        if (_runner is null) { return; }
        var snapshot = _runner.Snapshots.ReadLatest();
        var mm = _settlers.Multimesh;
        if (mm.InstanceCount != snapshot.Count) { mm.InstanceCount = snapshot.Count; }
        Span<int> counts = stackalloc int[_content!.Actions.Count + 1];
        Embody(snapshot, (float)delta);
        if (_play) { UpdatePlay(snapshot, (float)delta); }
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (_play) { counts[snapshot.Action[i] < 0 ? counts.Length - 1 : snapshot.Action[i]]++; continue; }
            if (snapshot.IsPlayer[i])
            {
                mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero));   // the "you" marker stands in
                continue;
            }

            var basis = new Basis(Vector3.Up, -snapshot.Yaw[i]);
            var at = _bodies.TryGetValue(snapshot.Ids[i], out var body) ? body : new Vector2(snapshot.X[i], snapshot.Z[i]);
            mm.SetInstanceTransform(i, new Transform3D(basis, new Vector3(at.X, Ground(at.X, at.Y) + 0.85f, at.Y)));
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
        var minute = snapshot.GameMs / Sim.Time.SimClock.MsPerGameMinute;
        UpdateSun(snapshot.Weather, minute);
        _overlay.Text = $"FeudalSim · {_scenarioId} · {date} · {snapshot.Weather.Sky} {Sim.Climate.Weather.AirTempC(snapshot.Weather, minute):F0} °C · wind {snapshot.Weather.WindMs:F0} m/s · step {snapshot.Step} · {_stepsPerSecond:F1} steps/s · ×{_timeScale} · {_runner.Mode}\n" +
                        $"{snapshot.Count - (snapshot.IsPlayer.AsSpan(0, snapshot.Count).Contains(true) ? 1 : 0)} settlers · [Space] pause · [1][2][4][8] speed · {(_play ? "WASD walk · mouse look · Shift jog · Ctrl sprint · [E] interact · [I] carry/eat · [V] view · [K] knap · [Esc] leave · [Tab] mouse" : "WASD/arrows pan · wheel zoom")}" +
                        (_aiStatus.Length > 0 ? $" · {_aiStatus}" : "");
        if (_play && snapshot.PlayerStaminaMax > 0f)
        {
            var state = (Sim.Health.VitalState)snapshot.PlayerVital switch { Sim.Health.VitalState.Dead => " · DEAD", Sim.Health.VitalState.Dying => " · DYING", Sim.Health.VitalState.Downed or Sim.Health.VitalState.Recovering => " · DOWN", _ => "" };
            _overlay.Text += $"\nyou: health {snapshot.PlayerHealth:F0} · blood {snapshot.PlayerBlood:F0} · stamina {snapshot.PlayerStamina:F0}/{snapshot.PlayerStaminaMax:F0}{(snapshot.PlayerWinded ? " (winded)" : "")} · warmth {snapshot.PlayerWarmth:F0} · wet {snapshot.PlayerWetness:F0}{state}";
        }

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
        if (_autotestIsland && _clock > 4) { IslandAutotest(); }
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
            if (snap.Tier[i] != 0 || snap.IsPlayer[i]) { continue; }
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
            if (_play && e.Payload is Sim.Events.TradeOffered or Sim.Events.TradeSettled or Sim.Events.NegotiationEnded) { OnTradeEvent(e.Payload); continue; }
            if (_play && OnKnapEvent(e.Payload)) { continue; }
            if (_play && OnNodeEvent(e.Payload)) { continue; }
            if (_play && OnEatEvent(e.Payload)) { continue; }
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
        if (_autotest || _play) { return; }
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
        if (_play && CameraInput(@event)) { return; }

        if (@event is InputEventMouseButton { Pressed: true } wheel && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            var forward = -_camera.GlobalTransform.Basis.Z;
            var step = wheel.ButtonIndex == MouseButton.WheelUp ? 3f : -3f;
            var next = _camera.Position + (forward * step);
            if (next.Y is > 4f and < 120f) { _camera.Position = next; }
            return;
        }

        if (_runner is null || @event is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        if (_play && InventoryKey(key.Keycode)) { return; }
        switch (key.Keycode)
        {
            case Key.Space: TogglePause(); break;
            case Key.Key1: SetSpeed(1); break;
            case Key.Key2: SetSpeed(2); break;
            case Key.Key4: SetSpeed(4); break;
            case Key.Key8: SetSpeed(8); break;
            case Key.E when _play && !Knapping: Interact(); break;
            case Key.Escape when _play && Knapping: _knap!.Escape(); break;
            case Key.Escape when _play: Leave(); break;
            case Key.K when _play: TryKnap(); break;
            case Key.P when _play: TogglePeople(); break;
            case Key.T when _play: TrySteal(); break;
            case Key.I when _play && !Knapping && _dialogue?.Conversation is null: ToggleInventory(); break;
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
        _uiPlayer?.Stop();
        _voicePlayer?.Stop();
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
        foreach (var player in new AudioStreamPlayer?[] { _uiPlayer }) { if (player is not null) { player.Stop(); player.Stream = null; } }
        if (_voicePlayer is not null) { _voicePlayer.Stop(); _voicePlayer.Stream = null; }
        foreach (var stream in _streams.Values) { stream.Dispose(); }
        _streams.Clear();
        if (_campfire is not null)
        {
            _campfire.Stop();   // a live playback keeps the stream referenced past shutdown
            _campfire.Stream?.Dispose();
            _campfire.Stream = null;
        }
    }
}
