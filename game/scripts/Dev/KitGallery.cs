using Godot;

namespace FeudalSim.Game.Dev;

/// <summary>M1-20: every graybox kit asset (res://assets/*/graybox) on a labelled grid — the import check and review sheet. `-- --shot out.png`.</summary>
public partial class KitGallery : Node3D
{
    private double _t;

    public override void _Ready()
    {
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.62f, 0.72f, 0.82f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.75f, 0.75f, 0.78f), AmbientLightEnergy = 0.7f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0), ShadowEnabled = true });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(80, 60) }, Position = new Vector3(20, 0, 10), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.48f, 0.33f) } });
        var files = new List<string>();
        foreach (var folder in new[] { "characters", "props", "buildings", "nature" })
        {
            var dir = DirAccess.Open($"res://assets/{folder}/graybox");
            if (dir is null) { continue; }
            files.AddRange(dir.GetFiles().Where(f => f.EndsWith(".glb", StringComparison.Ordinal)).Order().Select(f => $"res://assets/{folder}/graybox/{f}"));
        }

        var x = 0f;
        var row = 0;
        var placed = 0;
        foreach (var file in files)
        {
            var big = file.Contains("/buildings/", StringComparison.Ordinal);
            var cell = big ? 11f : 3f;
            if (x + cell > 46f) { (x, row) = (0f, row + 1); }
            var node = GD.Load<PackedScene>(file)?.Instantiate<Node3D>();
            if (node is null) { GD.PushError($"kit: cannot load {file}"); continue; }
            node.Position = new Vector3(x + (cell / 2), 0, row * 7f);
            AddChild(node);
            AddChild(new Label3D { Text = file.Split('/')[^1].Replace("_a.glb", "", StringComparison.Ordinal), Position = node.Position + new Vector3(0, 0.1f, 1.6f), RotationDegrees = new Vector3(-60, 0, 0), FontSize = 32, PixelSize = 0.012f, Modulate = Colors.White, OutlineSize = 8 });
            x += cell;
            placed++;
        }

        AddChild(new Camera3D { Position = new Vector3(23, 34, 34), RotationDegrees = new Vector3(-48, 0, 0), Current = true, Fov = 60 });
        GD.Print($"kit gallery: {placed} of {files.Count} assets loaded");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        var args = OS.GetCmdlineUserArgs();
        if (_t < 1.5 || Array.IndexOf(args, "--shot") is not (>= 0 and var i) || i + 1 >= args.Length) { return; }
        GetViewport().GetTexture().GetImage().SavePng(args[i + 1]);
        GetTree().Quit(0);
    }
}
