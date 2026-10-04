using FeudalSim.Sim.Content;
using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;
using Godot;

namespace FeudalSim.Game.World;

/// <summary>
/// M2-FP4: the Landfall camp and the wreck from the art agent's models (art brief P0.7–P0.8), placed at the sim's camp
/// places: the fire ring with flames, smoke and firelight; the sailcloth shelters (the camp's <c>ShelterSleeps</c> each,
/// in an arc facing the fire) with a bough bed in each and a lean-to; the salvaged stores; the firewood pile; the water
/// skin and bucket at the brook; the *Wending Star* in two halves on its reef with the mast and rigging, and flotsam on
/// the strands the generator chose. Everything sits on the 2 m ground. Missing files are skipped (the graybox stays).
/// </summary>
public static class CampDressing
{
    public static int Build(Node3D parent, Island island, in CampRecord camp, int people)
    {
        var root = new Node3D { Name = "Camp" };
        parent.AddChild(root);
        var placed = 0;
        Node3D? Put(string path, float x, float z, float yaw = 0f, float scale = 1f)
        {
            if (!ResourceLoader.Exists(path)) { return null; }
            var n = GD.Load<PackedScene>(path).Instantiate<Node3D>();
            n.Position = new Vector3(x, island.HeightAt(x, z), z);
            n.Rotation = new Vector3(0, yaw, 0);
            n.Scale = Vector3.One * scale;
            DropVertexColour(n);
            root.AddChild(n);
            placed++;
            return n;
        }

        float Face(float x, float z, float tx, float tz) => Mathf.Atan2(-(tx - x), -(tz - z)) + Mathf.Pi;   // models face −Z: turn their fronts to (tx, tz)

        // The fire and what burns in it.
        var (fx, fz) = camp.Place(PlaceKind.Fire);
        if (Put("res://assets/buildings/campfire.glb", fx, fz) is { } fire) { fire.AddChild(Flames()); }
        Put("res://assets/props/firewood_pile.glb", fx + 2.6f, fz + 0.8f, 0.4f);

        // Shelters in an arc around the shelter place, open sides to the fire; a bough bed in each.
        var (sx, sz) = camp.Place(PlaceKind.Shelter);
        var shelters = Math.Max(1, (int)MathF.Ceiling(people / (float)Math.Max(1, (int)camp.ShelterSleeps)));
        var toFire = Mathf.Atan2(fx - sx, fz - sz);
        for (var k = 0; k < shelters; k++)
        {
            var a = toFire + Mathf.Pi + ((k - ((shelters - 1) / 2f)) * 0.42f);
            var r = 7f + (k % 2 * 1.5f);
            float x = sx + (Mathf.Sin(a) * r), z = sz + (Mathf.Cos(a) * r);
            Put("res://assets/buildings/sailcloth_shelter.glb", x, z, Face(x, z, fx, fz));
            Put("res://assets/props/bough_bed.glb", x + (Mathf.Sin(a) * 0.6f), z + (Mathf.Cos(a) * 0.6f), Face(x, z, fx, fz) + (Mathf.Pi / 2));
        }

        Put("res://assets/buildings/lean_to.glb", sx + 4f, sz - 9f, Face(sx + 4f, sz - 9f, fx, fz));

        // The stores: what came ashore, stacked by the fire.
        var (tx, tz) = camp.Place(PlaceKind.Stores);
        string[] stores = ["crate", "crate", "barrel", "barrel", "sack", "sack", "sack", "chest"];
        for (var k = 0; k < stores.Length; k++)
        {
            var a = k * 0.8f;
            Put($"res://assets/props/{stores[k]}.glb", tx + (Mathf.Cos(a) * (1.1f + (k % 3 * 0.5f))), tz + (Mathf.Sin(a) * (1.1f + (k % 3 * 0.5f))), k * 0.7f);
        }

        // The water place.
        var (wx, wz) = camp.Place(PlaceKind.Water);
        Put("res://assets/props/wooden_bucket.glb", wx + 0.8f, wz + 0.4f);
        Put("res://assets/props/water_skin.glb", wx + 1.3f, wz - 0.3f, 1.1f);
        return placed;
    }

    /// <summary>The wreck from art when it exists (two hull halves heeled over on the reef, mast and rigging), and flotsam on the strands.</summary>
    public static bool Wreck(Node3D parent, Island island, LandingSite l, IReadOnlyList<Poi> pois)
    {
        if (!ResourceLoader.Exists("res://assets/wreck/hull_bow.glb")) { return false; }
        var root = new Node3D { Name = "WendingStar", Position = new Vector3(l.WreckX, 0, l.WreckZ) };
        var away = new Vector2(l.WreckX - l.BeachX, l.WreckZ - l.BeachZ).Normalized();
        root.Rotation = new Vector3(0, Mathf.Atan2(away.X, away.Y) + 0.6f, 0);
        parent.AddChild(root);
        void Part(string name, Vector3 at, float yaw = 0f)
        {
            var path = $"res://assets/wreck/{name}.glb";
            if (!ResourceLoader.Exists(path)) { return; }
            var n = GD.Load<PackedScene>(path).Instantiate<Node3D>();
            (n.Position, n.Rotation) = (at, new Vector3(0, yaw, 0));
            DropVertexColour(n);
            root.AddChild(n);
        }

        Part("reef_rocks", Vector3.Zero);
        Part("hull_bow", new Vector3(0, 0, -6f));
        Part("hull_stern", new Vector3(1.5f, 0, 7f), 0.25f);
        Part("mast_broken", new Vector3(-2f, 0, 1f), 0.9f);
        Part("rigging_tangle", new Vector3(-3.5f, 0, -1f), 0.4f);

        // Flotsam on the strands the generator chose (10 §3.9): a few pieces each, above the waterline.
        string[] flotsam = ["flotsam_planks", "flotsam_barrel", "flotsam_crate", "rope_coil", "sail_heap"];
        var k = 0;
        foreach (var p in pois.Where(p => p.Kind == PoiKind.Flotsam))
        {
            for (var j = 0; j < 3; j++, k++)
            {
                var path = $"res://assets/wreck/{flotsam[k % flotsam.Length]}.glb";
                if (!ResourceLoader.Exists(path)) { continue; }
                float x = p.X + ((j - 1) * 3.5f), z = p.Z + ((j % 2) * 2.5f);
                var n = GD.Load<PackedScene>(path).Instantiate<Node3D>();
                (n.Position, n.Rotation) = (new Vector3(x, Math.Max(0.2f, island.HeightAt(x, z)), z), new Vector3(0, k * 1.3f, 0));
                DropVertexColour(n);
                parent.AddChild(n);
            }
        }

        return true;
    }

    /// <summary>Flames, embers and smoke (GPU particles) and a flickering warm light, sized for a cooking fire.</summary>
    private static Node3D Flames()
    {
        var root = new Node3D { Name = "Fire", Position = new Vector3(0, 0.15f, 0) };
        GpuParticles3D Emitter(string name, int amount, float life, Color from, Color to, Vector3 velocity, float size, bool additive)
        {
            var mat = new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 1, 0), Spread = 18f, InitialVelocityMin = velocity.X, InitialVelocityMax = velocity.Y,
                Gravity = new Vector3(0.3f, velocity.Z, 0), EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.25f,
                ScaleMin = size * 0.7f, ScaleMax = size * 1.3f,
                ColorRamp = new GradientTexture1D { Gradient = new Gradient { Colors = [from, to], Offsets = [0f, 1f] } },
            };
            var quad = new QuadMesh { Size = new Vector2(0.4f, 0.4f) };
            quad.Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
                AlbedoTexture = Soft,
            };
            return new GpuParticles3D { Name = name, Amount = amount, Lifetime = life, ProcessMaterial = mat, DrawPass1 = quad, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        }

        root.AddChild(Emitter("Flames", 40, 0.7f, new Color(1f, 0.75f, 0.25f, 0.9f), new Color(0.9f, 0.2f, 0.05f, 0f), new Vector3(0.6f, 1.4f, 0.8f), 1.0f, true));
        root.AddChild(Emitter("Smoke", 24, 4.5f, new Color(0.35f, 0.33f, 0.3f, 0.45f), new Color(0.6f, 0.6f, 0.6f, 0f), new Vector3(0.6f, 1.0f, 0.3f), 2.4f, false));
        root.AddChild(new OmniLight3D { Name = "Firelight", LightColor = new Color(1f, 0.62f, 0.3f), LightEnergy = 2.2f, OmniRange = 9f, Position = new Vector3(0, 0.6f, 0), ShadowEnabled = false });
        return root;
    }

    /// <summary>A soft round puff (radial gradient), so flames and smoke aren't squares.</summary>
    private static readonly GradientTexture2D Soft = new()
    {
        Width = 64, Height = 64, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f),
        Gradient = new Gradient { Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.5f), new Color(1, 1, 1, 0)], Offsets = [0f, 0.45f, 1f] },
    };

    /// <summary>Art request: COLOR_0.R is a wind/cloth weight, not albedo.</summary>
    public static void DropVertexColour(Node n)
    {
        if (n is MeshInstance3D m && m.Mesh is { } mesh)
        {
            for (var s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                if (mesh.SurfaceGetMaterial(s) is StandardMaterial3D { VertexColorUseAsAlbedo: true } mat)
                {
                    var copy = (StandardMaterial3D)mat.Duplicate();
                    copy.VertexColorUseAsAlbedo = false;
                    m.SetSurfaceOverrideMaterial(s, copy);
                }
            }
        }

        foreach (var c in n.GetChildren()) { DropVertexColour(c); }
    }
}
