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
        AddChild(BuildMesh(heights));
        var meshMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        AddChild(BuildCollision(heights));
        var colMs = sw.Elapsed.TotalMilliseconds;
        GD.Print($"TerrainSpike: {Size}x{Size} @ {Spacing} m = {(Size - 1) * Spacing} m; heights {genMs:F0} ms, mesh {meshMs:F0} ms, collision {colMs:F0} ms; tris {(Size - 1) * (Size - 1) * 2}");

        _player = GetNode<CharacterBody3D>("Player");
        _cameraPivot = GetNode<Node3D>("Player/CameraPivot");
        _player.Position = new Vector3(0, SampleHeight(heights, 0, 0) + 3, 0);
        if (_autowalk) { DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled); }
    }

    private static MeshInstance3D BuildMesh(float[] h)
    {
        var half = (Size - 1) * Spacing / 2f;
        var verts = new Vector3[Size * Size];
        var normals = new Vector3[Size * Size];
        var colors = new Color[Size * Size];
        for (var z = 0; z < Size; z++)
        {
            for (var x = 0; x < Size; x++)
            {
                var i = (z * Size) + x;
                verts[i] = new Vector3((x * Spacing) - half, h[i], (z * Spacing) - half);
                var hl = h[(z * Size) + Math.Max(0, x - 1)];
                var hr = h[(z * Size) + Math.Min(Size - 1, x + 1)];
                var hd = h[(Math.Max(0, z - 1) * Size) + x];
                var hu = h[(Math.Min(Size - 1, z + 1) * Size) + x];
                var n = new Vector3(hl - hr, 2 * Spacing, hd - hu).Normalized();
                normals[i] = n;
                // Palette swatches (art/palettes): sand low, grass, moss, rock on steep/high ground.
                colors[i] = n.Y < 0.82f || h[i] > 27 ? new Color("7a7a80") : h[i] < 1.5f ? new Color("c8b48a") : h[i] < 14 ? new Color("6f8f3a") : new Color("5a6b2e");
            }
        }

        var indices = new int[(Size - 1) * (Size - 1) * 6];
        var k = 0;
        for (var z = 0; z < Size - 1; z++)
        {
            for (var x = 0; x < Size - 1; x++)
            {
                var a = (z * Size) + x;
                var b = a + 1;
                var c = a + Size;
                var d = c + 1;
                indices[k++] = a; indices[k++] = b; indices[k++] = c;
                indices[k++] = b; indices[k++] = d; indices[k++] = c;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = 0.95f });
        return new MeshInstance3D { Mesh = mesh, Name = "TerrainMesh" };
    }

    private static StaticBody3D BuildCollision(float[] h)
    {
        var shape = new HeightMapShape3D { MapWidth = Size, MapDepth = Size, MapData = h };
        var body = new StaticBody3D { Name = "TerrainBody" };
        // HeightMapShape3D samples are 1 unit apart and centred; scale X/Z to the vertex spacing.
        body.AddChild(new CollisionShape3D { Shape = shape, Scale = new Vector3(Spacing, 1, Spacing) });
        return body;
    }

    private static float SampleHeight(float[] h, float wx, float wz)
    {
        var half = (Size - 1) * Spacing / 2f;
        var x = Math.Clamp((int)MathF.Round((wx + half) / Spacing), 0, Size - 1);
        var z = Math.Clamp((int)MathF.Round((wz + half) / Spacing), 0, Size - 1);
        return h[(z * Size) + x];
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
