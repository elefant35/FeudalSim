using Godot;

namespace FeudalSim.Game.World;

/// <summary>
/// The typed facade over Terrain3D (ADR-0009 decision 2): the plugin has no C# bindings, so every name-based
/// Get/Set/Call lives here, used at setup and for edits — never per frame (gameplay heights come from the sim's
/// heightfield). It owns the integration gotchas found in M0-12: the map corner on the region grid, region_size after
/// entering the tree, free_editor_textures off for runtime assets, a texture asset so the colour map shows. It also
/// caches a world's imported data under <c>user://worlds/&lt;seed&gt;/</c> (20 §12.4) so later sessions skip generation.
/// </summary>
public sealed class Terrain3DFacade
{
    private readonly Node3D _node;
    private readonly GodotObject _data;

    private Terrain3DFacade(Node3D node, GodotObject data) => (_node, _data) = (node, data);

    public static bool Available => ClassDB.ClassExists("Terrain3D");

    public Node3D Node => _node;

    public string Version => _node.Call("get_version").AsString();

    public int RegionCount => _data.Call("get_region_count").AsInt32();

    public int RegionSize => _node.Get("region_size").AsInt32();

    /// <summary>Creates the node under <paramref name="parent"/> with the project's settings (2 m spacing, dynamic collision).</summary>
    public static Terrain3DFacade Create(Node parent, float spacingM, int regionSize, Camera3D camera, GodotObject assets)
    {
        if (!Available) { throw new InvalidOperationException("Terrain3D is not installed — run tools/godot/fetch_addons.sh"); }
        var node = ClassDB.Instantiate("Terrain3D").AsGodotObject() as Node3D ?? throw new InvalidOperationException("Terrain3D did not instantiate as a Node3D");
        node.Name = "Terrain3D";
        node.Set("vertex_spacing", spacingM);
        node.Set("collision_mode", 1);   // Dynamic / Game: collision built around the camera
        node.Set("mesh_lods", 7);
        node.Set("mesh_size", 48);
        node.Set("free_editor_textures", false);   // runtime-built assets have no path to reload from
        node.Set("assets", assets);
        parent.AddChild(node);   // material and data exist once in the tree; region_size only applies then
        node.Call("change_region_size", regionSize);
        node.Get("material").AsGodotObject().Set("world_background", 0);   // no infinite plane outside the regions
        node.Call("set_camera", camera);
        return new Terrain3DFacade(node, node.Get("data").AsGodotObject());
    }

    /// <summary>
    /// Imports a square height map (and colour map) whose corner is at (-half, -half). The image must cover whole
    /// regions and the corner must sit on the region grid (import_images snaps slices to the region containing them).
    /// </summary>
    public void Import(Image heights, Image? colors, float halfExtentM)
        => _data.Call("import_images", new Godot.Collections.Array { heights, default, colors ?? default(Variant) }, new Vector3(-halfExtentM, 0, -halfExtentM), 0f, 1f);

    /// <summary>Height through the plugin (≈ 1 µs a call via Variant marshalling): setup checks only.</summary>
    public float HeightAt(Vector3 at) => _data.Call("get_height", at).AsSingle();

    public static string CacheDir(ulong seed) => $"user://worlds/{seed}/terrain";

    /// <summary>True if a cached world was found and loaded (Terrain3D region files, one per region).</summary>
    public bool TryLoadCache(ulong seed)
    {
        var dir = CacheDir(seed);
        if (!DirAccess.DirExistsAbsolute(dir) || DirAccess.GetFilesAt(dir).Length == 0) { return false; }
        if (_data.HasMethod("load_directory")) { _data.Call("load_directory", dir); }
        else { _node.Set("data_directory", dir); }   // older API: the node loads its data directory
        return RegionCount > 0;
    }

    public void SaveCache(ulong seed)
    {
        var dir = CacheDir(seed);
        DirAccess.MakeDirRecursiveAbsolute(dir);
        _data.Call("save_directory", dir);
    }
}
