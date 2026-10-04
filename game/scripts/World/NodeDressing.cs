using FeudalSim.Sim.Content;
using FeudalSim.Sim.WorldGen;
using Godot;

namespace FeudalSim.Game.World;

/// <summary>
/// M2-FP3: the island's trees, bushes, rocks and plants, drawn from the sim's own nodes (10 §3.8, 20 §6.6). Chunks
/// within <see cref="RadiusM"/> of the player are regenerated with the sim's integer-only scatter (same seed, same
/// table, so client and sim agree on every node) and drawn as one MultiMesh per model part. Models follow the art
/// contract's paths (<c>flora/trees/&lt;node&gt;_&lt;v&gt;.glb</c>, <c>flora/bushes</c>, <c>flora/plants</c>,
/// <c>nature/rocks</c>); a node type without art gets a graybox stand-in. The variant and yaw come from a hash of
/// (chunk, index); trees scale by size class (×0.3 / 0.6 / 1.0 / 1.25 of the timber model). The sim's deltas set what is
/// felled (a stump) or picked (hidden until it regrows). Rebuilt when the player crosses into another chunk or a delta
/// changes. Presentation only: nothing here feeds back into the sim except the commands the player issues.
/// </summary>
public sealed class NodeDressing
{
    public const float RadiusM = 224f, SmallRadiusM = 128f;
    public const byte Felled = 2, Harvested = 3;   // Processes.NodeFelled, Foraging.NodeHarvested
    private static readonly float[] TreeScale = [0.3f, 0.6f, 1f, 1.25f];

    private readonly Node3D _root;
    private readonly Island _island;
    private readonly ContentDatabase _content;
    private readonly NodeScatter.Table _table;
    private readonly int _per;
    private readonly Model[][] _models;   // per node type: its variants
    private readonly Model _stump;
    private readonly List<ResourceNode> _buffer = [];
    private readonly Dictionary<int, List<ResourceNode>> _chunks = [];
    private readonly Dictionary<(int Chunk, int Index), byte> _states = [];
    private readonly Dictionary<Mesh, MultiMeshInstance3D> _drawn = [];
    private readonly Dictionary<Mesh, List<Transform3D>> _batch = [];
    private (int Cx, int Cz) _center = (int.MinValue, 0);
    private readonly Model[] _grass, _ferns;

    /// <summary>Ground cover (no sim nodes): grass tufts and bracken within this radius, by biome.</summary>
    public const float CoverRadiusM = 44f;

    /// <summary>A model: its mesh parts with their local transforms (from a .glb, or a graybox).</summary>
    private sealed record Model(List<(Mesh Mesh, Transform3D Local)> Parts, bool Shadows, bool Art);

    /// <summary>Solid things the player can't walk through near the camera: trunks and boulders (x, z, radius).</summary>
    public List<(float X, float Z, float R)> Solids { get; } = [];

    /// <summary>Nodes near the player: position, chunk, index, type, state (for look-at interaction).</summary>
    public List<(Vector3 At, int Chunk, int Index, int Type, byte Size, byte State)> Near { get; } = [];

    public int ArtTypes { get; }
    public int Instances { get; private set; }

    public NodeDressing(Node3D parent, Island island, ContentDatabase content)
    {
        _root = new Node3D { Name = "Nodes" };
        parent.AddChild(_root);
        (_island, _content) = (island, content);
        _table = new NodeScatter.Table(content);
        _per = NodeScatter.ChunksPerSide(island.Map.Grid);
        _models = new Model[content.Nodes.Count][];
        for (var t = 0; t < content.Nodes.Count; t++)
        {
            var def = content.Nodes[t];
            var art = LoadVariants(def);
            if (art.Length > 0) { ArtTypes++; }
            _models[t] = art.Length > 0 ? art : [Graybox(def.Kind)];
        }

        _grass = LoadDecor("grass_tuft");
        _ferns = LoadDecor("fern");
        _stump = new Model([(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.4f, Height = 0.5f, RadialSegments = 7 }, new Transform3D(Basis.Identity, new Vector3(0, 0.25f, 0)))], true, false);
        foreach (var part in _stump.Parts) { part.Mesh.SurfaceSetMaterial(0, Mat("6a4a30")); }
    }

    private static Model[] LoadDecor(string name)
    {
        var models = new List<Model>();
        for (var v = 'a'; v <= 'h'; v++)
        {
            var path = $"res://assets/flora/plants/{name}_{v}.glb";
            if (!ResourceLoader.Exists(path)) { break; }
            models.Add(FromScene(GD.Load<PackedScene>(path), false, true));
        }

        return [.. models];
    }

    private static string Folder(NodeKind kind) => kind switch
    {
        NodeKind.Tree => "res://assets/flora/trees/",
        NodeKind.Bush => "res://assets/flora/bushes/",
        NodeKind.Rock => "res://assets/nature/rocks/",
        _ => "res://assets/flora/plants/",
    };

    private static Model[] LoadVariants(NodeDef def)
    {
        var name = def.Id["node.".Length..];
        var models = new List<Model>();
        for (var v = 'a'; v <= 'h'; v++)
        {
            var path = $"{Folder(def.Kind)}{name}_{v}.glb";
            if (!ResourceLoader.Exists(path)) { break; }
            models.Add(FromScene(GD.Load<PackedScene>(path), def.Kind is NodeKind.Tree or NodeKind.Rock or NodeKind.Bush, def.Kind != NodeKind.Rock));
        }

        return [.. models];
    }

    /// <summary>The visible meshes of a .glb with their transforms relative to its root; collision objects are skipped.</summary>
    private static Model FromScene(PackedScene scene, bool shadows, bool sway)
    {
        var root = scene.Instantiate<Node3D>();
        var parts = new List<(Mesh, Transform3D)>();
        void Walk(Node n, Transform3D parent)
        {
            if (n is CollisionObject3D || n.Name.ToString().Contains("-col", StringComparison.Ordinal)) { return; }
            var t = n is Node3D n3 ? parent * n3.Transform : parent;
            if (n is MeshInstance3D mi && mi.Mesh is { } mesh && mi.Visible) { parts.Add((Fixed(mesh, sway), t)); }
            foreach (var c in n.GetChildren()) { Walk(c, t); }
        }

        Walk(root, Transform3D.Identity);
        root.Free();
        return new Model(parts, shadows, true);
    }

    /// <summary>
    /// Art request (STATUS.md): COLOR_0.R is a wind weight, not albedo. Foliage gets the wind shader (which reads it);
    /// everything else just drops vertex-colour albedo.
    /// </summary>
    private static Mesh Fixed(Mesh mesh, bool sway)
    {
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            if (mesh.SurfaceGetMaterial(s) is not StandardMaterial3D m) { continue; }
            if (sway)
            {
                var wind = new ShaderMaterial { Shader = WindShader };
                wind.SetShaderParameter("albedo_tex", m.AlbedoTexture);
                wind.SetShaderParameter("albedo_color", m.AlbedoColor);
                mesh.SurfaceSetMaterial(s, wind);
                WindMaterials.Add(wind);
            }
            else if (m.VertexColorUseAsAlbedo)
            {
                var copy = (StandardMaterial3D)m.Duplicate();
                copy.VertexColorUseAsAlbedo = false;
                mesh.SurfaceSetMaterial(s, copy);
            }
        }

        return mesh;
    }

    private static readonly Shader WindShader = GD.Load<Shader>("res://shaders/foliage_wind.gdshader");
    private static readonly List<ShaderMaterial> WindMaterials = [];

    /// <summary>Sets the sway from the sim's wind (m/s): calm 0.03 m, a gale ≈ 0.25 m at the tips.</summary>
    public static void SetWind(float windMs)
    {
        var strength = Math.Clamp(0.03f + (windMs * 0.012f), 0.03f, 0.25f);
        foreach (var m in WindMaterials) { m.SetShaderParameter("strength", strength); }
    }

    private static StandardMaterial3D Mat(string hex) => new() { AlbedoColor = new Color(hex), Roughness = 0.95f };

    /// <summary>Stand-ins for node types the art agent hasn't delivered yet (palette colours, simple shapes).</summary>
    private static Model Graybox(NodeKind kind)
    {
        var parts = new List<(Mesh, Transform3D)>();
        switch (kind)
        {
            case NodeKind.Tree:
                var trunk = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.32f, Height = 7f, RadialSegments = 6 };
                trunk.SurfaceSetMaterial(0, Mat("5a4130"));
                var crown = new CylinderMesh { TopRadius = 0f, BottomRadius = 3.2f, Height = 10f, RadialSegments = 7 };
                crown.SurfaceSetMaterial(0, Mat("3f5a2c"));
                parts.Add((trunk, new Transform3D(Basis.Identity, new Vector3(0, 3.5f, 0))));
                parts.Add((crown, new Transform3D(Basis.Identity, new Vector3(0, 10f, 0))));
                break;
            case NodeKind.Bush:
                var bush = new SphereMesh { Radius = 1f, Height = 1.4f, RadialSegments = 8, Rings = 4 };
                bush.SurfaceSetMaterial(0, Mat("4a6a30"));
                parts.Add((bush, new Transform3D(Basis.Identity, new Vector3(0, 0.6f, 0))));
                break;
            case NodeKind.Rock:
                var rock = new SphereMesh { Radius = 0.5f, Height = 0.6f, RadialSegments = 6, Rings = 3 };
                rock.SurfaceSetMaterial(0, Mat("7a7a80"));
                parts.Add((rock, new Transform3D(Basis.Identity, new Vector3(0, 0.15f, 0))));
                break;
            default:
                var plant = new SphereMesh { Radius = 0.3f, Height = 0.4f, RadialSegments = 6, Rings = 3 };
                plant.SurfaceSetMaterial(0, Mat("6f8f3a"));
                parts.Add((plant, new Transform3D(Basis.Identity, new Vector3(0, 0.15f, 0))));
                break;
        }

        return new Model(parts, kind != NodeKind.Patch, false);
    }

    public int ChunkOf(float x, float z, out int cx, out int cz)
    {
        var half = (_island.Map.Grid.Size - 1) * _island.Map.Grid.CellM / 2f;
        cx = Math.Clamp((int)MathF.Floor((x + half) / NodeScatter.ChunkM), 0, _per - 1);
        cz = Math.Clamp((int)MathF.Floor((z + half) / NodeScatter.ChunkM), 0, _per - 1);
        return (cz * _per) + cx;
    }

    /// <summary>The chunks to show around a point (the sim's chunk ids), for the delta query.</summary>
    public List<int> ChunksAround(float x, float z)
    {
        ChunkOf(x, z, out var cx, out var cz);
        var r = (int)MathF.Ceiling(RadiusM / NodeScatter.ChunkM);
        var list = new List<int>();
        for (var dz = -r; dz <= r; dz++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                int x2 = cx + dx, z2 = cz + dz;
                if (x2 >= 0 && z2 >= 0 && x2 < _per && z2 < _per && ((dx * dx) + (dz * dz)) * NodeScatter.ChunkM * NodeScatter.ChunkM <= (RadiusM + 64) * (RadiusM + 64)) { list.Add((z2 * _per) + x2); }
            }
        }

        return list;
    }

    /// <summary>Generated node counts per chunk (so the sim can be asked for their states).</summary>
    public int CountIn(int chunk)
    {
        if (!_chunks.TryGetValue(chunk, out var list))
        {
            NodeScatter.Chunk(_island.Map.Grid, _island.Map.AttemptSeed, _table, chunk % _per, chunk / _per, _buffer);
            _chunks[chunk] = list = [.. _buffer];
        }

        return list.Count;
    }

    /// <summary>New node states from the sim (chunk, index → state); rebuilds when anything changed.</summary>
    public void SetStates(IEnumerable<(int Chunk, int Index, byte State)> states, float px, float pz)
    {
        var changed = false;
        foreach (var (c, i, s) in states)
        {
            var had = _states.GetValueOrDefault((c, i));
            if (had == s) { continue; }
            if (s == 0) { _states.Remove((c, i)); } else { _states[(c, i)] = s; }
            changed = true;
        }

        if (changed) { Rebuild(px, pz); }
    }

    /// <summary>Rebuild now (the ground cover follows the player every few metres).</summary>
    public void Refresh(float px, float pz) => Rebuild(px, pz);

    /// <summary>Rebuild when the player's chunk changes.</summary>
    public bool Update(float px, float pz)
    {
        ChunkOf(px, pz, out var cx, out var cz);
        if ((cx, cz) == _center) { return false; }
        Rebuild(px, pz);
        return true;
    }

    private void Rebuild(float px, float pz)
    {
        ChunkOf(px, pz, out var ccx, out var ccz);
        _center = (ccx, ccz);
        foreach (var list in _batch.Values) { list.Clear(); }
        Solids.Clear();
        Near.Clear();
        var grid = _island.Map.Grid;
        var instances = 0;
        foreach (var chunk in ChunksAround(px, pz))
        {
            CountIn(chunk);
            var nodes = _chunks[chunk];
            int cx = chunk % _per, cz = chunk / _per;
            for (var k = 0; k < nodes.Count; k++)
            {
                var node = nodes[k];
                var def = _content.Nodes[node.Type];
                var (x, z) = NodeScatter.Position(grid, cx, cz, node);
                var d2 = ((x - px) * (x - px)) + ((z - pz) * (z - pz));
                var small = def.Kind is NodeKind.Patch;
                if (d2 > (small ? SmallRadiusM * SmallRadiusM / 2.5f : def.Kind == NodeKind.Tree ? RadiusM * RadiusM : SmallRadiusM * SmallRadiusM)) { continue; }
                var state = _states.GetValueOrDefault((chunk, k));
                if (state == Harvested && small) { continue; }   // picked: gone until it regrows (spring)
                var h = Hash((uint)chunk, (uint)k);
                var variants = _models[node.Type];
                var model = def.Kind == NodeKind.Tree && state == Felled ? _stump : variants[(int)(h % (uint)variants.Length)];
                var scale = def.Kind == NodeKind.Tree && state != Felled ? TreeScale[Math.Min(3, (int)node.Size)] : 1f;
                scale *= 0.9f + (0.2f * ((h >> 8) & 0xFF) / 255f);
                var yaw = ((h >> 16) & 0xFFFF) / 65535f * Mathf.Tau;
                var y = _island.HeightAt(x, z) - (def.Kind == NodeKind.Tree ? 0.1f : 0.02f);
                var place = new Transform3D(new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale), new Vector3(x, y, z));
                foreach (var (mesh, local) in model.Parts)
                {
                    if (!_batch.TryGetValue(mesh, out var list)) { _batch[mesh] = list = []; }
                    list.Add(place * local);
                }

                instances++;
                if (d2 < 30f * 30f)
                {
                    if (def.Kind == NodeKind.Tree) { Solids.Add((x, z, 0.35f * scale + 0.15f)); }
                    else if (def.Id == "node.boulder") { Solids.Add((x, z, 0.9f)); }
                    Near.Add((new Vector3(x, y, z), chunk, k, node.Type, node.Size, state));
                }
            }
        }

        Cover(px, pz);
        foreach (var (mesh, list) in _batch)
        {
            if (!_drawn.TryGetValue(mesh, out var mmi))
            {
                mmi = new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh } };
                _root.AddChild(mmi);
                _drawn[mesh] = mmi;
            }

            var mm = mmi.Multimesh;
            mm.InstanceCount = list.Count;
            for (var i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, list[i]); }
            mmi.CastShadow = list.Count > 0 && mesh.GetAabb().Size.Y > 1.2f ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        }

        Instances = instances;
    }

    /// <summary>
    /// Grass tufts and bracken around the player (presentation only): per 4 m cell of open ground a hashed count by
    /// biome (meadow and valley 4 tufts, dunes 3, moor 2; broadleaf 2 ferns + 1 tuft, pine 2 ferns), none on the strand,
    /// in streams or on steep rock.
    /// </summary>
    private void Cover(float px, float pz)
    {
        if (_grass.Length == 0 && _ferns.Length == 0) { return; }
        var g = _island.Map.Grid;
        const float cell = 4f;
        var r = (int)(CoverRadiusM / cell);
        int ox = (int)MathF.Floor(px / cell), oz = (int)MathF.Floor(pz / cell);
        for (var dz = -r; dz <= r; dz++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                if ((dx * dx) + (dz * dz) > r * r) { continue; }
                int cx = ox + dx, cz = oz + dz;
                float x0 = cx * cell, z0 = cz * cell;
                var gc = Hosting.CampAnchor.Cell(g, x0, z0);
                if (gc < 0 || g.Land[gc] == 0 || g.Water[gc] != 0 || g.Slope[gc] > 30) { continue; }
                var (tufts, ferns) = (Biome)g.Biome[gc] switch
                {
                    Biome.Meadow or Biome.RiverValley => (4, 0), Biome.CoastDunes => (3, 0), Biome.HillsMoor => (2, 0),
                    Biome.Broadleaf => (1, 2), Biome.Pine => (0, 2), Biome.Wetland => (2, 0), _ => (1, 0),
                };
                for (var k = 0; k < tufts + ferns; k++)
                {
                    var h = Hash((uint)cx * 7919u + 13u, ((uint)cz * 104729u) + (uint)k);
                    float x = x0 + ((h & 0xFF) / 255f * cell), z = z0 + (((h >> 8) & 0xFF) / 255f * cell);
                    var ground = _island.HeightAt(x, z);
                    if (ground < 1.8f) { continue; }   // not on the strand
                    var set = k < tufts ? _grass : _ferns;
                    if (set.Length == 0) { continue; }
                    var model = set[(int)((h >> 16) % (uint)set.Length)];
                    var scale = 0.8f + (0.5f * ((h >> 24) & 0xFF) / 255f);
                    var place = new Transform3D(new Basis(Vector3.Up, ((h >> 4) & 0xFFF) / 4095f * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3(x, ground - 0.03f, z));
                    foreach (var (mesh, local) in model.Parts)
                    {
                        if (!_batch.TryGetValue(mesh, out var list)) { _batch[mesh] = list = []; }
                        list.Add(place * local);
                    }
                }
            }
        }
    }

    /// <summary>The nearest loaded node matching a test (chunk, index, position), or null.</summary>
    public (int Chunk, int Index, Vector3 At)? Nearest(float px, float pz, Func<NodeDef, ResourceNode, byte, bool> test)
    {
        (int, int, Vector3)? best = null;
        var bestD = float.MaxValue;
        foreach (var (chunk, nodes) in _chunks)
        {
            for (var k = 0; k < nodes.Count; k++)
            {
                var n = nodes[k];
                if (!test(_content.Nodes[n.Type], n, _states.GetValueOrDefault((chunk, k)))) { continue; }
                var (x, z) = NodeScatter.Position(_island.Map.Grid, chunk % _per, chunk / _per, n);
                var d = ((x - px) * (x - px)) + ((z - pz) * (z - pz));
                if (d < bestD) { (best, bestD) = ((chunk, k, new Vector3(x, _island.HeightAt(x, z), z)), d); }
            }
        }

        return best;
    }

    /// <summary>True if a step to (x, z) would walk into a trunk or a boulder.</summary>
    public bool Blocked(float x, float z)
    {
        foreach (var (sx, sz, r) in Solids)
        {
            if (((x - sx) * (x - sx)) + ((z - sz) * (z - sz)) < r * r) { return true; }
        }

        return false;
    }

    private static uint Hash(uint a, uint b)
    {
        var h = (a * 0x9E3779B1u) ^ (b * 0x85EBCA77u);
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return h;
    }
}
