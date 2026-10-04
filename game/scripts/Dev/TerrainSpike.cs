using System.Diagnostics;
using FeudalSim.Game.World;
using FeudalSim.Sim.World;
using Godot;

namespace FeudalSim.Game.Dev;

/// <summary>
/// M0-12 terrain spike (20 §12.4, ADR-0009): one sim-generated heightfield at 2 m spacing, rendered either as
/// an ArrayMesh + HeightMapShape3D (approach B, default) or with Terrain3D (approach A, `--terrain3d`; needs
/// tools/godot/fetch_addons.sh), and a third-person capsule that walks on it.
/// Args after `--`: `--terrain3d`, `--samples N` (per side; 257 = 512 m, 4097 = 8,192 m; Terrain3D only above
/// 257), `--autowalk` (walk a circle with vsync off for 20 s, log fps, GPU frame time, VRAM, hitches and C#
/// interop cost, save a screenshot to user://terrain_spike[_t3d].png and quit), `--check` (with --terrain3d: build,
/// verify Terrain3D heights match the sim heightfield, exit 0/1 — works with --headless; used by godot.yml).
/// M1-S4: `--cache` loads the world from user://worlds/42/terrain when present (else generates in parallel and saves
/// it); `--traverse [speed m/s]` runs a serpentine across all 16 regions of the 8 km map and reports hitches > 50 ms.
/// </summary>
public partial class TerrainSpike : Node3D
{
    private const float Spacing = 2f;      // metres (20 §12.4 recommends 2 m vertex spacing)
    private int _size = 257;               // samples per side
    private float[] _heights = [];
    private bool _terrain3d;
    private GodotObject? _t3dData;
    private CharacterBody3D _player = null!;
    private Node3D _cameraPivot = null!;
    private bool _autowalk;
    private double _elapsed;
    private readonly List<double> _fps = [];
    private readonly List<double> _gpuMs = [];
    private double _maxFrameMs;
    private double _sampleTimer;
    private string _label = "";
    private float _alignErr = float.NaN;
    private Terrain3DFacade? _t3d;
    private bool _traverse;
    private float _traverseSpeed = 60f;
    private readonly List<Vector2> _route = [];
    private int _routeAt;
    private readonly List<double> _frameMs = new(65536);
    private int _regionsVisited;
    private readonly HashSet<(int, int)> _regions = [];

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        _autowalk = args.Contains("--autowalk");
        _traverse = args.Contains("--traverse");
        var ts = Array.IndexOf(args, "--traverse");
        if (ts >= 0 && ts + 1 < args.Length && float.TryParse(args[ts + 1], System.Globalization.CultureInfo.InvariantCulture, out var speed)) { _traverseSpeed = speed; }
        _terrain3d = args.Contains("--terrain3d");
        var at = Array.IndexOf(args, "--samples");
        if (at >= 0 && at + 1 < args.Length) { _size = int.Parse(args[at + 1], System.Globalization.CultureInfo.InvariantCulture); }
        if (!_terrain3d && _size > 257) { GD.PushError("ArrayMesh approach is single-mesh; use --terrain3d for larger maps"); GetTree().Quit(1); return; }

        _player = GetNode<CharacterBody3D>("Player");
        _cameraPivot = GetNode<Node3D>("Player/CameraPivot");
        var sw = Stopwatch.StartNew();
        using (var jobs = new FeudalSim.Hosting.JobRunner(Math.Max(1, System.Environment.ProcessorCount - 1)))
        {
            _heights = Heightfield.Generate(42, _size, Spacing, jobs: jobs);   // M1-S4: chunked, parallel, identical to serial
        }

        var genMs = sw.Elapsed.TotalMilliseconds;
        var extent = (_size - 1) * Spacing;
        float spawnY;
        if (_terrain3d)
        {
            _label = "Terrain3D";
            spawnY = BuildTerrain3D(genMs) + 3;
        }
        else
        {
            _label = "ArrayMesh + HeightMapShape3D";
            sw.Restart();
            AddChild(TerrainBuilder.BuildMesh(_heights, _size, Spacing));
            var meshMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            AddChild(TerrainBuilder.BuildCollision(_heights, _size, Spacing));
            GD.Print($"TerrainSpike[B]: {_size}² @ {Spacing} m = {extent} m; heights {genMs:F0} ms, mesh {meshMs:F0} ms, collision {sw.Elapsed.TotalMilliseconds:F0} ms; tris {(_size - 1) * (_size - 1) * 2}");
            spawnY = TerrainBuilder.SampleHeight(_heights, _size, Spacing, 0, 0) + 3;
        }

        if (_terrain3d && args.Contains("--check"))
        {
            var ok = _alignErr < 0.01f;
            GD.Print($"TerrainSpike: CHECK {(ok ? "PASS" : "FAIL")} · Terrain3D heights vs sim heightfield max error {_alignErr:F3} m");
            GetTree().Quit(ok ? 0 : 1);
            return;
        }

        _player.Position = new Vector3(0, spawnY, 0);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        if (_autowalk) { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); }
        if (_traverse)
        {
            // A serpentine through the middle of every 2,048 m region row: 4 rows × 8 km, so all 16 regions are crossed.
            var edge = ((_size - 1) * Spacing / 2f) - 64f;
            for (var row = 0; row < 4; row++)
            {
                var z = -edge + (row * 2048f) + 960f;
                _route.Add(new Vector2(row % 2 == 0 ? -edge : edge, z));
                _route.Add(new Vector2(row % 2 == 0 ? edge : -edge, z));
            }

            _player.Position = new Vector3(_route[0].X, SampleSim(_route[0].X, _route[0].Y) + 2f, _route[0].Y);
            _routeAt = 1;
            GD.Print($"TerrainSpike: traverse {_route.Count} waypoints at {_traverseSpeed} m/s ≈ {RouteLength() / _traverseSpeed:F0} s");
        }
    }

    private float SampleSim(float x, float z) => TerrainBuilder.SampleHeight(_heights, _size, Spacing, x, z);

    private float RouteLength()
    {
        var length = 0f;
        for (var i = 1; i < _route.Count; i++) { length += _route[i - 1].DistanceTo(_route[i]); }
        return length;
    }

    /// <summary>Approach A: Terrain3D through the typed facade (World/Terrain3DFacade.cs).</summary>
    private float BuildTerrain3D(double genMs)
    {
        if (!Terrain3DFacade.Available)
        {
            GD.PushError("Terrain3D is not installed — run tools/godot/fetch_addons.sh");
            GetTree().Quit(1);
            return 0;
        }

        // Terrain3D tiles the world into square regions of region_size vertices, and import_images puts each slice
        // into the region *containing* its position (found in this spike, terrain_3d_data.cpp): the map corner must
        // sit on the region grid or the whole map shifts. 512 m map → 128-vertex (256 m) regions; 8 km → 1024 (2 km).
        // The image covers (size-1)² vertices, a whole number of regions; the far edge row comes from the neighbour.
        var img = _size - 1;
        var regionSize = img <= 256 ? 128 : 1024;
        var sw = Stopwatch.StartNew();
        var heightBytes = new byte[img * img * 4];
        var colorBytes = new byte[img * img * 4];
        for (var z = 0; z < img; z++)
        {
            Buffer.BlockCopy(_heights, z * _size * 4, heightBytes, z * img * 4, img * 4);
            for (var x = 0; x < img; x++)
            {
                var c = TerrainBuilder.ColorAt(_heights, _size, Spacing, x, z, out _);
                var o = ((z * img) + x) * 4;
                (colorBytes[o], colorBytes[o + 1], colorBytes[o + 2], colorBytes[o + 3]) = ((byte)c.R8, (byte)c.G8, (byte)c.B8, 128);   // A = roughness modifier (neutral)
            }
        }

        var heightImg = Image.CreateFromData(img, img, false, Image.Format.Rf, heightBytes);
        var colorImg = Image.CreateFromData(img, img, false, Image.Format.Rgba8, colorBytes);
        var imagesMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        _t3d = Terrain3DFacade.Create(this, Spacing, regionSize, GetNode<Camera3D>("Player/CameraPivot/Camera"), DetailTextureAssets());
        var terrain = _t3d.Node;
        var nodeMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var half = (_size - 1) * Spacing / 2f;
        _t3dData = terrain.Get("data").AsGodotObject();
        var cached = OS.GetCmdlineUserArgs().Contains("--cache") && _t3d.TryLoadCache(42);
        if (!cached) { _t3d.Import(heightImg, colorImg, half); }
        var importMs = sw.Elapsed.TotalMilliseconds;
        if (!cached && OS.GetCmdlineUserArgs().Contains("--cache"))
        {
            sw.Restart();
            _t3d.SaveCache(42);
            GD.Print($"TerrainSpike: cached world → {ProjectSettings.GlobalizePath(Terrain3DFacade.CacheDir(42))} in {sw.Elapsed.TotalMilliseconds:F0} ms");
        }

        // Alignment: Terrain3D heights must match the sim heightfield the rest of the game uses.
        var maxErr = 0f;
        for (var i = 0; i < 64; i++)
        {
            var x = (i * 7919 % (img - 1)) * Spacing - half;
            var z = (i * 104729 % (img - 1)) * Spacing - half;
            var t3d = _t3d.HeightAt(new Vector3(x, 0, z));
            maxErr = Math.Max(maxErr, Math.Abs(t3d - TerrainBuilder.SampleHeight(_heights, _size, Spacing, x, z)));
        }

        _alignErr = maxErr;
        GD.Print($"TerrainSpike[A]: Terrain3D {_t3d.Version} · {_size}² @ {Spacing} m = {(_size - 1) * Spacing} m · regions {_t3d.RegionCount} × {_t3d.RegionSize} · heights {genMs:F0} ms ({System.Environment.ProcessorCount - 1} threads), images {imagesMs:F0} ms, node {nodeMs:F0} ms, {(cached ? "cache load" : "import")} {importMs:F0} ms · height match max error {maxErr:F3} m over 64 points");
        return _t3d.HeightAt(Vector3.Zero);
    }

    /// <summary>
    /// One near-white detail texture (albedo+height, normal+roughness packed as Terrain3D expects) tinted by the
    /// palette colour map — the same look as the ArrayMesh vertex colours. Without any texture asset Terrain3D draws a
    /// debug checkerboard and ignores the colour map, which would make the GPU comparison unfair.
    /// </summary>
    private static GodotObject DetailTextureAssets()
    {
        const int n = 256;
        var albedo = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var normal = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var hash = (uint)((x * 73856093) ^ (y * 19349663)) * 2654435761u;   // deterministic grain, no RNG
                var v = 0.88f + (0.12f * ((hash >> 24) / 255f));
                albedo.SetPixel(x, y, new Color(v, v, v, 0.5f));   // A = height for blending
                normal.SetPixel(x, y, new Color(0.5f, 0.5f, 1f, 0.9f));   // flat normal, A = roughness
            }
        }

        albedo.GenerateMipmaps();
        normal.GenerateMipmaps();
        var texture = ClassDB.Instantiate("Terrain3DTextureAsset").AsGodotObject();
        texture.Set("name", "palette_detail");
        texture.Set("albedo_texture", ImageTexture.CreateFromImage(albedo));
        texture.Set("normal_texture", ImageTexture.CreateFromImage(normal));
        texture.Set("uv_scale", 0.25f);
        var assets = ClassDB.Instantiate("Terrain3DAssets").AsGodotObject();
        assets.Call("set_texture", 0, texture);
        return assets;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_traverse) { Traverse((float)delta); return; }
        var dir = Vector3.Zero;
        if (_autowalk)
        {
            var t = _elapsed * 0.05;
            dir = new Vector3((float)Math.Cos(t), 0, (float)Math.Sin(t));
        }
        else
        {
            var input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
            dir = (_cameraPivot.GlobalBasis * new Vector3(input.X, 0, input.Y)) with { Y = 0 };
        }

        var speed = _autowalk || Input.IsKeyPressed(Key.Shift) ? 6.5f : 4.0f;   // canon §10.9 jog / sprint
        var v = _player.Velocity;
        v.X = dir.Normalized().X * speed;
        v.Z = dir.Normalized().Z * speed;
        v.Y = _player.IsOnFloor() ? 0 : v.Y - (9.8f * (float)delta);
        _player.Velocity = v;
        _player.MoveAndSlide();
        if (dir.LengthSquared() > 0.01f) { _player.GetNode<Node3D>("Body").Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0); }
    }

    /// <summary>M1-S4: fly the route at speed, kept on the sim heightfield (the camera drives Terrain3D's clipmap and collision).</summary>
    private void Traverse(float delta)
    {
        if (_routeAt >= _route.Count) { return; }
        var at = new Vector2(_player.Position.X, _player.Position.Z);
        var to = _route[_routeAt] - at;
        var step = _traverseSpeed * delta;
        if (to.Length() <= step) { at = _route[_routeAt]; _routeAt++; } else { at += to.Normalized() * step; }
        _player.Position = new Vector3(at.X, SampleSim(at.X, at.Y) + 1f, at.Y);
        if (to.LengthSquared() > 0.01f) { _player.GetNode<Node3D>("Body").Rotation = new Vector3(0, Mathf.Atan2(-to.X, -to.Y), 0); }
        var half = (_size - 1) * Spacing / 2f;
        _regions.Add(((int)((at.X + half) / 2048f), (int)((at.Y + half) / 2048f)));
        _regionsVisited = _regions.Count;
        if (_routeAt >= _route.Count) { TraverseReport(); }
    }

    private void TraverseReport()
    {
        var mb = 1024.0 * 1024.0;
        var frames = _frameMs.Count;
        _frameMs.Sort();
        double Q(double q) => _frameMs[Math.Min(frames - 1, (int)Math.Ceiling(q * frames) - 1)];
        var hitches = _frameMs.Count(f => f > 50);
        GD.Print(FormattableString.Invariant($"TerrainSpike: TRAVERSE {(hitches == 0 ? "PASS" : "FAIL")} · {RouteLength() / 1000:F1} km at {_traverseSpeed} m/s in {_elapsed:F0} s · regions crossed {_regionsVisited}/16 · frames {frames} · frame ms p50 {Q(0.5):F2} p99 {Q(0.99):F2} p99.9 {Q(0.999):F2} max {_frameMs[^1]:F1} · hitches > 50 ms {hitches}, > 33 ms {_frameMs.Count(f => f > 33)} · VRAM {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / mb:F0} MB (textures {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / mb:F0}, buffers {Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / mb:F0})"));
        GetTree().Quit(hitches == 0 ? 0 : 1);
    }

    public override void _Process(double delta)
    {
        if (_traverse && _elapsed > 3) { _frameMs.Add(delta * 1000); }
        _elapsed += delta;
        _sampleTimer += delta;
        if (_elapsed > 3) { _maxFrameMs = Math.Max(_maxFrameMs, delta * 1000); }
        if (_sampleTimer >= 1)
        {
            _sampleTimer = 0;
            _fps.Add(Engine.GetFramesPerSecond());
            _gpuMs.Add(RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()));
            GetNode<Label>("Overlay/Label").Text = $"Terrain spike ({_label}) · {Engine.GetFramesPerSecond():F0} fps · GPU {_gpuMs[^1]:F2} ms · player {_player.Position.X:F0}, {_player.Position.Y:F1}, {_player.Position.Z:F0} · floor {_player.IsOnFloor()}";
        }

        if (_autowalk && _elapsed > 20) { Report(); }
    }

    private void Report()
    {
        _autowalk = false;
        var fps = _fps.Skip(3).ToList();   // drop warm-up seconds
        var gpu = _gpuMs.Skip(3).ToList();
        var mb = 1024.0 * 1024.0;
        GD.Print($"TerrainSpike: {_label} · autowalk {fps.Count} s · fps min {fps.Min():F0} avg {fps.Average():F0} · GPU frame avg {gpu.Average():F2} ms max {gpu.Max():F2} ms · worst frame {_maxFrameMs:F1} ms · " +
                 $"VRAM {Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / mb:F0} MB (textures {Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed) / mb:F0}, buffers {Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed) / mb:F0}) · " +
                 $"draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):F0} · primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame):F0} · on floor {_player.IsOnFloor()} at y {_player.Position.Y:F1}");

        // C# interop cost: a height query through the engine (Variant marshalling) vs a plain C# array lookup.
        const int n = 20_000;
        var sw = Stopwatch.StartNew();
        var sink = 0f;
        for (var i = 0; i < n; i++) { sink += TerrainBuilder.SampleHeight(_heights, _size, Spacing, i % 200, i % 170); }
        var csNs = sw.Elapsed.TotalNanoseconds / n;
        if (_t3dData is not null)
        {
            sw.Restart();
            for (var i = 0; i < n; i++) { sink += _t3dData.Call("get_height", new Vector3(i % 200, 0, i % 170)).AsSingle(); }
            GD.Print($"TerrainSpike: interop · Terrain3D get_height via Call {sw.Elapsed.TotalNanoseconds / n:F0} ns/call vs C# array {csNs:F0} ns ({sink:F0})");
        }

        var shot = _terrain3d ? "user://terrain_spike_t3d.png" : "user://terrain_spike.png";
        GetViewport().GetTexture().GetImage().SavePng(shot);
        GD.Print($"TerrainSpike: screenshot {ProjectSettings.GlobalizePath(shot)}");
        GetTree().Quit();
    }
}
