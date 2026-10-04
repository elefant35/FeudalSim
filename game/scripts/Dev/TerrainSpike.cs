using System.Diagnostics;
using FeudalSim.Sim.World;
using Godot;

namespace FeudalSim.Game.Dev;

/// <summary>
/// M0-12 terrain spike, approach B (20 §12.4): a sim-generated 512 m heightfield rendered as an ArrayMesh with a
/// HeightMapShape3D for collision, and a third-person capsule that walks on it. Run with `-- --autowalk` to walk a
/// circle with vsync off, log frame rates, save a screenshot to user://terrain_spike.png and quit.
/// </summary>
public partial class TerrainSpike : Node3D
{
    private const int Size = 257;          // samples per side → 256 cells
    private const float Spacing = 2f;      // metres (20 §12.4 recommends 2 m vertex spacing)
    private CharacterBody3D _player = null!;
    private Node3D _cameraPivot = null!;
    private bool _autowalk;
    private double _elapsed;
    private readonly List<double> _fps = [];
    private double _sampleTimer;

    public override void _Ready()
    {
        _autowalk = OS.GetCmdlineUserArgs().Contains("--autowalk");
        var sw = Stopwatch.StartNew();
        var heights = Heightfield.Generate(42, Size, Spacing);
        var genMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        AddChild(TerrainBuilder.BuildMesh(heights, Size, Spacing));
        var meshMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        AddChild(TerrainBuilder.BuildCollision(heights, Size, Spacing));
        var colMs = sw.Elapsed.TotalMilliseconds;
        GD.Print($"TerrainSpike: {Size}x{Size} @ {Spacing} m = {(Size - 1) * Spacing} m; heights {genMs:F0} ms, mesh {meshMs:F0} ms, collision {colMs:F0} ms; tris {(Size - 1) * (Size - 1) * 2}");

        _player = GetNode<CharacterBody3D>("Player");
        _cameraPivot = GetNode<Node3D>("Player/CameraPivot");
        _player.Position = new Vector3(0, TerrainBuilder.SampleHeight(heights, Size, Spacing, 0, 0) + 3, 0);
        if (_autowalk) { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); }
    }

    public override void _PhysicsProcess(double delta)
    {
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

        var speed = Input.IsKeyPressed(Key.Shift) ? 6.5f : 4.0f;   // canon §10.9 jog / sprint
        var v = _player.Velocity;
        v.X = dir.Normalized().X * speed;
        v.Z = dir.Normalized().Z * speed;
        v.Y = _player.IsOnFloor() ? 0 : v.Y - (9.8f * (float)delta);
        _player.Velocity = v;
        _player.MoveAndSlide();
        if (dir.LengthSquared() > 0.01f) { _player.GetNode<Node3D>("Body").Rotation = new Vector3(0, Mathf.Atan2(-dir.X, -dir.Z), 0); }
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        _sampleTimer += delta;
        if (_sampleTimer >= 1)
        {
            _sampleTimer = 0;
            _fps.Add(Engine.GetFramesPerSecond());
            GetNode<Label>("Overlay/Label").Text = $"Terrain spike (ArrayMesh + HeightMapShape3D) · {Engine.GetFramesPerSecond():F0} fps · player {_player.Position.X:F0}, {_player.Position.Y:F1}, {_player.Position.Z:F0} · floor {_player.IsOnFloor()}";
        }

        if (_autowalk && _elapsed > 20)
        {
            var samples = _fps.Skip(3).ToList();   // drop warm-up seconds
            GD.Print($"TerrainSpike: autowalk {samples.Count} s, fps min {samples.Min():F0} avg {samples.Average():F0}, player on floor {_player.IsOnFloor()} at y {_player.Position.Y:F1}");
            GetViewport().GetTexture().GetImage().SavePng("user://terrain_spike.png");
            GD.Print($"TerrainSpike: screenshot {ProjectSettings.GlobalizePath("user://terrain_spike.png")}");
            GetTree().Quit();
        }
    }
}
