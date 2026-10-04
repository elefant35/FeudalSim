using Godot;

namespace FeudalSim.Game.Dev;

/// <summary>
/// Spike M1-S1 (30): N skinned, animated low-poly characters (art/generators/humanoid) walking and idling under a
/// shadowed sun; logs frame-time percentiles after a warm-up and quits.
/// `-- --count 150 --seconds 30` (village) · `-- --battle` (300 packed into a 60 m field, all walking).
/// </summary>
public partial class CrowdSpike : Node3D
{
    private const string Model = "res://assets/characters/humanoid_a.glb";
    private readonly List<(Node3D Body, float Radius, float Angle, float Speed, bool Walking)> _people = [];
    private readonly List<double> _frameMs = new(16384);
    private double _elapsed, _seconds = 30;
    private int _count = 150;
    private bool _battle;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        int Arg(string name, int fallback) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : fallback;
        _battle = args.Contains("--battle");
        _count = Arg("--count", _battle ? 300 : 150);
        _seconds = Arg("--seconds", 30);

        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.62f, 0.72f, 0.82f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.7f, 0.72f, 0.75f), AmbientLightEnergy = 0.6f,
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0), ShadowEnabled = true, LightEnergy = 1.1f });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(400, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.45f, 0.28f) } });
        var spread = _battle ? 30f : 70f;
        AddChild(new Camera3D { Position = new Vector3(0, _battle ? 28 : 45, spread * 1.3f), RotationDegrees = new Vector3(_battle ? -40 : -35, 0, 0), Current = true, Far = 600 });

        var scene = GD.Load<PackedScene>(Model);
        var rng = new RandomNumberGenerator { Seed = 7 };
        for (var i = 0; i < _count; i++)
        {
            var body = scene.Instantiate<Node3D>();
            AddChild(body);
            var walking = _battle || i % 3 != 0;
            var player = FindPlayer(body);
            if (player is not null)
            {
                var name = walking ? "walk" : "idle";
                var anim = player.GetAnimation(name);
                if (anim is not null) { anim.LoopMode = Animation.LoopModeEnum.Linear; }
                player.Play(name);
                player.Seek(rng.RandfRange(0, (float)(anim?.Length ?? 1)), true);
                player.SpeedScale = rng.RandfRange(0.9f, 1.1f);
            }

            _people.Add((body, rng.RandfRange(3f, spread), rng.RandfRange(0, Mathf.Tau), walking ? rng.RandfRange(1.2f, 1.6f) : 0f, walking));
        }

        GD.Print($"crowd: {_count} characters ({(_battle ? "battle" : "village")}), model {Model}");
    }

    private static AnimationPlayer? FindPlayer(Node n)
    {
        if (n is AnimationPlayer p) { return p; }
        foreach (var c in n.GetChildren()) { if (FindPlayer(c) is { } found) { return found; } }
        return null;
    }

    public override void _Process(double delta)
    {
        for (var i = 0; i < _people.Count; i++)
        {
            var (body, r, a, speed, walking) = _people[i];
            if (walking) { a += (float)(speed / r * delta); _people[i] = (body, r, a, speed, walking); }
            var pos = new Vector3(r * Mathf.Cos(a), 0, r * Mathf.Sin(a));
            body.Position = pos;
            body.Rotation = new Vector3(0, -a, 0);   // tangent to the circle; the model faces +Z
        }

        _elapsed += delta;
        if (_elapsed < 3.0) { return; }
        _frameMs.Add(delta * 1000.0);
        if (_elapsed < 3.0 + _seconds) { return; }
        if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--shot") is var shot and >= 0 && shot + 1 < OS.GetCmdlineUserArgs().Length)
        {
            GetViewport().GetTexture().GetImage().SavePng(OS.GetCmdlineUserArgs()[shot + 1]);
        }

        _frameMs.Sort();
        double Q(double q) => _frameMs[Math.Min(_frameMs.Count - 1, (int)Math.Ceiling(q * _frameMs.Count) - 1)];
        var over60 = _frameMs.Count(f => f > 1000.0 / 60.0) / (double)_frameMs.Count;
        var over30 = _frameMs.Count(f => f > 1000.0 / 30.0) / (double)_frameMs.Count;
        GD.Print(FormattableString.Invariant($"crowd fps: {_count} characters · {_frameMs.Count} frames in {_seconds:F0} s · mean {_frameMs.Count / _seconds:F1} fps · frame ms p50 {Q(0.5):F2} p95 {Q(0.95):F2} p99 {Q(0.99):F2} max {_frameMs[^1]:F1} · frames > 16.7 ms {over60:P2} · > 33.3 ms {over30:P2} · draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} · primitives {Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame)}"));
        GetTree().Quit(0);
    }
}
