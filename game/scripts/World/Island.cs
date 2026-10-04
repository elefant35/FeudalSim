using System.Diagnostics;
using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;
using Godot;

namespace FeudalSim.Game.World;

/// <summary>
/// M2-FP1: the generated island in the client. Heights are the sim map's 2 m upsample (10 §3.2; key = the map's attempt
/// seed, so every reader of the 2 m field gets the same heights), drawn by Terrain3D (ADR-0009) with a colour map from
/// the biome, shore and slope grids until the art agent's splat layers land (M2-FP4). A sea plane at 0 m, a sky with fog,
/// and a graybox wreck on its reef. Terrain3D's imported regions are cached under <c>user://worlds/&lt;seed&gt;/terrain</c>,
/// keyed by the map fingerprint and <see cref="TerrainVersion"/> (bump it when the colouring changes).
/// Gameplay heights (bodies, markers, the camera) come from <see cref="HeightAt"/>, a plain array lookup.
/// </summary>
public sealed class Island
{
    public const string TerrainVersion = "fp1-3";
    public const float Spacing = 2f;

    private readonly float[] _h;
    private readonly int _n;
    private readonly float _half;

    private Island(float[] heights, int n) => (_h, _n, _half) = (heights, n, (n - 1) * Spacing / 2f);

    public WorldMap Map { get; private init; } = null!;
    public Terrain3DFacade? Terrain { get; private set; }
    public string Report { get; private set; } = "";

    /// <summary>Bilinear height of the 2 m field at a world position (the sea floor offshore; 0 m is sea level).</summary>
    public float HeightAt(float x, float z)
    {
        var fx = Math.Clamp((x + _half) / Spacing, 0f, _n - 1.001f);
        var fz = Math.Clamp((z + _half) / Spacing, 0f, _n - 1.001f);
        int c = (int)fx, r = (int)fz;
        float ox = fx - c, oz = fz - r;
        var i = (r * _n) + c;
        return (_h[i] * (1 - ox) * (1 - oz)) + (_h[i + 1] * ox * (1 - oz)) + (_h[i + _n] * (1 - ox) * oz) + (_h[i + _n + 1] * ox * oz);
    }

    /// <summary>Standing height: the ground, or the sea surface where the ground is under water (people wade and swim at 0 m).</summary>
    public float StandAt(float x, float z) => MathF.Max(HeightAt(x, z), -0.9f);

    public static Island Build(Node3D parent, WorldMap map, Camera3D camera, FeudalSim.Sim.IJobScheduler jobs)
    {
        var sw = Stopwatch.StartNew();
        var heights = Erosion.Upsample2m(map.Grid, map.AttemptSeed, jobs);
        var n = ((map.Grid.Size - 1) * (int)(map.Grid.CellM / Spacing)) + 1;
        var island = new Island(heights, n) { Map = map };
        var upMs = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        var cached = false;
        if (Terrain3DFacade.Available)
        {
            island.Terrain = Terrain3DFacade.Create(parent, Spacing, 1024, camera, DetailAssets());
            var key = $"{TerrainVersion} {map.Fingerprint:x16}";
            var keyFile = Terrain3DFacade.CacheDir(map.Seed) + "/island.key";
            cached = Godot.FileAccess.FileExists(keyFile) && Godot.FileAccess.GetFileAsString(keyFile) == key && island.Terrain.TryLoadCache(map.Seed);
            if (!cached)
            {
                island.Terrain.Import(island.HeightImage(), island.ColorImage(), island._half);
                island.Terrain.SaveCache(map.Seed);
                using var f = Godot.FileAccess.Open(keyFile, Godot.FileAccess.ModeFlags.Write);
                f.StoreString(key);
            }
        }
        else
        {
            GD.PushError("Terrain3D is not installed — run tools/godot/fetch_addons.sh (the island is drawn without terrain)");
        }

        var terrainMs = sw.Elapsed.TotalMilliseconds;
        parent.AddChild(Sea());
        if (map.Landing is { } l) { parent.AddChild(Wreck(l, island)); }
        island.Report = $"island: seed {map.Seed} · {n}² @ {Spacing} m · upsample {upMs:F0} ms · terrain {(cached ? "cache" : "import")} {terrainMs:F0} ms";
        GD.Print("SimHost: " + island.Report);
        return island;
    }

    private Image HeightImage()
    {
        var img = _n - 1;   // whole regions; the far edge row comes from the neighbour (see TerrainSpike)
        var bytes = new byte[img * img * 4];
        for (var z = 0; z < img; z++) { Buffer.BlockCopy(_h, z * _n * 4, bytes, z * img * 4, img * 4); }
        return Image.CreateFromData(img, img, false, Image.Format.Rf, bytes);
    }

    /// <summary>
    /// The colour map: palette swatches by biome blended bilinearly between 8 m cells (no stair steps), sand only on the
    /// strand (below 1.6 m within 40 m of the shore), mud along streams, rock on steep ground, wet sand below 0.4 m, and a
    /// ±6 % low-frequency mottle so open ground isn't flat colour.
    /// </summary>
    private Image ColorImage()
    {
        var img = _n - 1;
        var bytes = new byte[img * img * 4];
        var g = Map.Grid;
        var per = g.CellM / Spacing;
        Parallel.For(0, img, z =>
        {
            for (var x = 0; x < img; x++)
            {
                var i = (z * _n) + x;
                var h = _h[i];
                var dx = _h[i + 1] - _h[i];
                var dz = _h[i + _n] - _h[i];
                var slope = MathF.Sqrt((dx * dx) + (dz * dz)) / Spacing;   // tan of the slope
                float gx = x / per, gz = z / per;
                int c0 = Math.Min((int)gx, g.Size - 2), r0 = Math.Min((int)gz, g.Size - 2);
                float ox = gx - c0, oz = gz - r0;
                Color Cell(int c, int r)
                {
                    var k = (r * g.Size) + c;
                    if ((WaterClass)g.Water[k] is WaterClass.Stream or WaterClass.River) { return Mud; }
                    if (g.Land[k] == 0) { return WetSand; }
                    return (Biome)g.Biome[k] switch
                    {
                        Biome.CoastDunes => DuneGrass, Biome.Meadow => Grass, Biome.Broadleaf => ForestFloor, Biome.Pine => Needles,
                        Biome.Wetland => Wet, Biome.RiverValley => Grass, Biome.HillsMoor => Heather, Biome.Highland => Upland, _ => Grass,
                    };
                }

                var c = Cell(c0, r0).Lerp(Cell(c0 + 1, r0), ox).Lerp(Cell(c0, r0 + 1).Lerp(Cell(c0 + 1, r0 + 1), ox), oz);
                var k0 = (r0 * g.Size) + c0;
                var coast = (g.CoastDistM[k0] * (1 - ox) * (1 - oz)) + (g.CoastDistM[k0 + 1] * ox * (1 - oz)) + (g.CoastDistM[k0 + g.Size] * (1 - ox) * oz) + (g.CoastDistM[k0 + g.Size + 1] * ox * oz);
                if (h < 1.6f && coast < 40f) { c = c.Lerp(Sand, Math.Clamp((1.6f - h) / 0.8f, 0f, 1f)); }
                if (h < 0.4f) { c = WetSand; }
                if (slope > 0.45f) { c = c.Lerp(Rock, Math.Clamp((slope - 0.45f) / 0.25f, 0f, 1f)); }
                var mottle = 1f + (0.06f * (Noise(x / 9f, z / 9f) - 0.5f) * 2f);
                c = new Color(c.R * mottle, c.G * mottle, c.B * mottle);
                var o = ((z * img) + x) * 4;
                (bytes[o], bytes[o + 1], bytes[o + 2], bytes[o + 3]) = ((byte)c.R8, (byte)c.G8, (byte)c.B8, 128);   // A = roughness modifier (neutral)
            }
        });
        return Image.CreateFromData(img, img, false, Image.Format.Rgba8, bytes);
    }

    /// <summary>Smooth value noise in [0, 1] from an integer hash (presentation only; no RNG state).</summary>
    private static float Noise(float x, float z)
    {
        int xi = (int)MathF.Floor(x), zi = (int)MathF.Floor(z);
        float fx = x - xi, fz = z - zi;
        fx = fx * fx * (3 - (2 * fx));
        fz = fz * fz * (3 - (2 * fz));
        static float H(int a, int b) => ((uint)((a * 73856093) ^ (b * 19349663)) * 2654435761u >> 8) / 16777216f;
        return (H(xi, zi) * (1 - fx) * (1 - fz)) + (H(xi + 1, zi) * fx * (1 - fz)) + (H(xi, zi + 1) * (1 - fx) * fz) + (H(xi + 1, zi + 1) * fx * fz);
    }

    // Swatches from art/palettes/palette.json (earth, foliage and stone rows), slightly muted for ground.
    private static readonly Color WetSand = new("a8946a"), Sand = new("c8b48a"), DuneGrass = new("9a9a5a"), Grass = new("6f8f3a"),
        ForestFloor = new("5a5a2e"), Needles = new("5e4a2c"), Wet = new("4f5f34"), Heather = new("6a5a4a"), Upland = new("7a7a62"),
        Rock = new("7a7a80"), Mud = new("5a4130");

    /// <summary>One near-white detail texture tinted by the colour map (the TerrainSpike look) until the splat layers land.</summary>
    private static GodotObject DetailAssets()
    {
        const int n = 256;
        var albedo = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        var normal = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                var hash = (uint)((x * 73856093) ^ (y * 19349663)) * 2654435761u;
                var v = 0.86f + (0.14f * ((hash >> 24) / 255f));
                albedo.SetPixel(x, y, new Color(v, v, v, 0.5f));
                normal.SetPixel(x, y, new Color(0.5f, 0.5f, 1f, 0.92f));
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

    /// <summary>The sea: one large plane at 0 m (shader water and foam are M2-FP4's).</summary>
    private static MeshInstance3D Sea() => new()
    {
        Name = "Sea",
        Mesh = new PlaneMesh { Size = new Vector2(24_000, 24_000) },
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.30f, 0.36f, 0.82f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.12f, Metallic = 0.1f, MetallicSpecular = 0.7f,
        },
    };

    /// <summary>A graybox stand-in for the *Wending Star* (art brief P0.8): two hull halves heeled over on the reef, a broken mast.</summary>
    private static Node3D Wreck(LandingSite l, Island island)
    {
        var root = new Node3D { Name = "Wreck", Position = new Vector3(l.WreckX, 0, l.WreckZ) };
        var wood = new StandardMaterial3D { AlbedoColor = new Color("4a3424"), Roughness = 0.9f };
        var sail = new StandardMaterial3D { AlbedoColor = new Color("cfc6ae"), Roughness = 1f };
        var away = new Vector2(l.WreckX - l.BeachX, l.WreckZ - l.BeachZ).Normalized();
        root.Rotation = new Vector3(0, Mathf.Atan2(away.X, away.Y) + 0.5f, 0);
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(5.5f, 2.6f, 10f) }, MaterialOverride = wood, Position = new Vector3(0, 0.6f, -5.5f), RotationDegrees = new Vector3(4, 0, 24) });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(5f, 2.4f, 8f) }, MaterialOverride = wood, Position = new Vector3(1.2f, 0.2f, 5.5f), RotationDegrees = new Vector3(-6, 14, -18) });
        root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.22f, Height = 9f }, MaterialOverride = wood, Position = new Vector3(-1.5f, 4.2f, -4f), RotationDegrees = new Vector3(0, 0, 30) });
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(4f, 0.05f, 3f) }, MaterialOverride = sail, Position = new Vector3(-3.4f, 1.2f, -2f), RotationDegrees = new Vector3(20, 10, 60) });
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 3f, Height = 2.4f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("3a3a3e") }, Position = new Vector3(0, -0.9f, 0), Scale = new Vector3(2.2f, 1, 3.6f) });   // the reef
        root.AddChild(new Label3D { Text = "the Wending Star", Position = new Vector3(0, 8, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 64, OutlineSize = 16, PixelSize = 0.05f });
        return root;
    }

    /// <summary>Sky and fog for the island (the overhead and flat camps keep the scene's plain background).</summary>
    public static void Atmosphere(WorldEnvironment env)
    {
        var e = env.Environment;
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("5f7f9e"), SkyHorizonColor = new Color("a9b4bc"), GroundBottomColor = new Color("3a4248"), GroundHorizonColor = new Color("b9c4cc"),
            SunAngleMax = 30f,
        };
        e.BackgroundMode = Godot.Environment.BGMode.Sky;
        e.Sky = new Sky { SkyMaterial = sky };
        e.AmbientLightSource = Godot.Environment.AmbientSource.Sky;
        e.AmbientLightEnergy = 0.7f;
        e.FogEnabled = true;
        e.FogLightColor = new Color("aeb8c0");
        e.FogDensity = 0.0005f;
        e.FogSkyAffect = 0.35f;
        e.TonemapMode = Godot.Environment.ToneMapper.Filmic;
    }
}
