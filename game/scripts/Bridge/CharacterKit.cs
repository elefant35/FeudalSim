using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// M2-FP4: the art agent's modular settlers (art brief P0.1–P0.2): one body file per sex on the shared 40-bone rig with
/// every head, hair, beard and garment as a separate object, and five animation files whose clips bind to any body by
/// bone name. A person's look is chosen by a hash of their id (head, hair, beard, hood up or down), so it is stable
/// across sessions and identical for everyone who sees them. The player's own head parts can be hidden for first person.
/// </summary>
public sealed class CharacterKit
{
    public const string Male = "res://assets/characters/body_male.glb", Female = "res://assets/characters/body_female.glb";
    private static readonly string[] AnimFiles = ["locomotion", "rest", "social", "needs", "work"];
    private static readonly HashSet<string> Loops = ["idle", "idle_alt", "walk", "jog", "sprint", "crouch_walk", "carry_walk", "carry_log_walk", "talk_a", "talk_b", "talk_c", "argue_a", "argue_b"];

    private readonly PackedScene _male, _female;
    private readonly AnimationLibrary _library = new();

    public int Clips { get; }

    private CharacterKit(PackedScene male, PackedScene female)
    {
        (_male, _female) = (male, female);
        foreach (var file in AnimFiles)
        {
            var path = $"res://assets/characters/anims/{file}.glb";
            if (!ResourceLoader.Exists(path)) { continue; }
            var scene = GD.Load<PackedScene>(path).Instantiate<Node>();
            if (Find(scene) is { } player)
            {
                foreach (var lib in player.GetAnimationLibraryList())
                {
                    var source = player.GetAnimationLibrary(lib);
                    foreach (var name in source.GetAnimationList())
                    {
                        var anim = (Animation)source.GetAnimation(name).Duplicate();
                        if (Loops.Contains(name) || name.ToString().EndsWith("_loop", StringComparison.Ordinal)) { anim.LoopMode = Animation.LoopModeEnum.Linear; }
                        _library.AddAnimation(name, anim);
                        Clips++;
                    }
                }
            }

            scene.Free();
        }
    }

    /// <summary>The kit, if the art is in the project (else the legacy stand-in is used).</summary>
    public static CharacterKit? Load()
        => ResourceLoader.Exists(Male) && ResourceLoader.Exists(Female) ? new CharacterKit(GD.Load<PackedScene>(Male), GD.Load<PackedScene>(Female)) : null;

    public static AnimationPlayer? Find(Node n)
    {
        if (n is AnimationPlayer p) { return p; }
        foreach (var c in n.GetChildren()) { if (Find(c) is { } f) { return f; } }
        return null;
    }

    /// <summary>A body for a person: sex 0 male / 1 female; parts by hash of the id. Returns the head parts (for first person).</summary>
    /// <param name="worn">The item ids the sim says they wear (11 §9.2): each shows as its <c>Cloth_&lt;name&gt;</c> object; the rest are hidden.</param>
    public (Node3D Body, AnimationPlayer Anim, List<MeshInstance3D> HeadParts) Spawn(byte sex, ulong id, IReadOnlyList<string> worn)
    {
        var wearing = worn.Select(i => "Cloth_" + (i.StartsWith("item.", StringComparison.Ordinal) ? i[5..] : i)).ToHashSet(StringComparer.Ordinal);
        var body = (sex == 1 ? _female : _male).Instantiate<Node3D>();
        var meshes = new List<MeshInstance3D>();
        Collect(body, meshes);
        var h = Hash(id);
        string Pick(string prefix)
        {
            var options = meshes.Where(m => m.Name.ToString().StartsWith(prefix, StringComparison.Ordinal)).Select(m => m.Name.ToString()).OrderBy(n => n, StringComparer.Ordinal).ToList();
            return options.Count == 0 ? "" : options[(int)(h % (uint)options.Count)];
        }

        var head = Pick("Head_");
        h = Hash(h);
        var hair = Pick("Hair_");
        h = Hash(h);
        var beard = sex == 0 && h % 10 < 7 ? Pick("Beard_") : "";
        h = Hash(h);
        var hoodUp = h % 10 < 2;
        var heads = new List<MeshInstance3D>();
        foreach (var m in meshes)
        {
            var name = m.Name.ToString();
            m.Visible = name.StartsWith("Body_", StringComparison.Ordinal) || wearing.Contains(name)
                || name == head || name == beard || (name == hair && !hoodUp) || (hoodUp && name.StartsWith("Headwear_", StringComparison.Ordinal));
            if (hoodUp && name == "Cloth_wool_hood") { m.Visible = false; }   // the raised hood replaces the lowered one
            if (m.Visible && (name.StartsWith("Head_", StringComparison.Ordinal) || name.StartsWith("Hair_", StringComparison.Ordinal) || name.StartsWith("Beard_", StringComparison.Ordinal) || name.StartsWith("Headwear_", StringComparison.Ordinal))) { heads.Add(m); }   // the chosen head parts
            DropVertexColour(m);
        }

        var anim = new AnimationPlayer { Name = "Anim" };
        body.AddChild(anim);
        anim.AddAnimationLibrary("", _library);
        if (anim.HasAnimation("idle")) { anim.Play("idle"); }
        return (body, anim, heads);
    }

    /// <summary>
    /// `--facing-check`: measures, in the body's own space, which way the face points (the nose side of the head mesh) and
    /// which way each locomotion clip travels (the planted foot slides backward relative to the body, so travel is the
    /// opposite of the planted foot's motion). Prints both as ±Z.
    /// </summary>
    public string FacingReport(Node parent)
    {
        var lines = new List<string>();
        var (body, anim, _) = Spawn(0, 1, []);
        parent.AddChild(body);
        var meshes = new List<MeshInstance3D>();
        Collect(body, meshes);
        var head = meshes.First(m => m.Name.ToString().StartsWith("Head_", StringComparison.Ordinal));
        var verts = head.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var c = verts.Aggregate(Vector3.Zero, (a, v) => a + v) / verts.Length;
        float maxZ = verts.Max(v => v.Z) - c.Z, minZ = c.Z - verts.Min(v => v.Z);
        lines.Add($"face: head mesh extends +Z {maxZ:F3} m, −Z {minZ:F3} m → nose toward {(maxZ > minZ ? "+Z" : "−Z")}");
        var skeleton = body.FindChildren("*", "Skeleton3D", true, false).OfType<Skeleton3D>().First();
        var foot = skeleton.FindBone("LeftFoot");
        foreach (var clip in new[] { "walk", "jog", "sprint" })
        {
            if (!anim.HasAnimation(clip)) { continue; }
            anim.Play(clip);
            var len = anim.GetAnimation(clip).Length;
            var samples = new List<(float Y, float Z)>();
            for (var k = 0; k <= 60; k++)
            {
                anim.Seek(len * k / 60.0, true);
                var p = skeleton.GetBoneGlobalPose(foot).Origin;
                samples.Add((p.Y, p.Z));
            }

            var low = samples.Min(s => s.Y);
            float slide = 0;
            for (var k = 1; k < samples.Count; k++)
            {
                if (samples[k].Y < low + 0.02f && samples[k - 1].Y < low + 0.02f) { slide += samples[k].Z - samples[k - 1].Z; }
            }

            lines.Add($"{clip}: planted foot slides {slide:+0.000;-0.000} m along Z → travels toward {(slide < 0 ? "+Z" : "−Z")}");
        }

        body.QueueFree();
        return string.Join("\n", lines);
    }

    private static void Collect(Node n, List<MeshInstance3D> into)
    {
        if (n is MeshInstance3D m) { into.Add(m); }
        foreach (var c in n.GetChildren()) { Collect(c, into); }
    }

    /// <summary>Art request: COLOR_0.R is a wind/cloth weight, not albedo.</summary>
    private static void DropVertexColour(MeshInstance3D m)
    {
        if (m.Mesh is null) { return; }
        for (var s = 0; s < m.Mesh.GetSurfaceCount(); s++)
        {
            if (m.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D { VertexColorUseAsAlbedo: true } mat)
            {
                var copy = (StandardMaterial3D)mat.Duplicate();
                copy.VertexColorUseAsAlbedo = false;
                m.SetSurfaceOverrideMaterial(s, copy);
            }
        }
    }

    private static uint Hash(ulong x)
    {
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        return (uint)x;
    }

    /// <summary>The clip for what a person is doing (action id from content, or a state), with its playback speed.</summary>
    public static (string Clip, float Speed) ClipFor(string? action, bool lying, float metresPerSecond, ulong id)
    {
        if (lying) { return ("sleep_loop", 1f); }
        if (metresPerSecond > 0.3f)
        {
            return metresPerSecond > 5.2f ? ("sprint", metresPerSecond / 6.5f) : metresPerSecond > 2.6f ? ("jog", metresPerSecond / 4.0f) : ("walk", metresPerSecond / 1.6f);
        }

        var talk = (id % 3) switch { 0 => "talk_a", 1 => "talk_b", _ => "talk_c" };
        return (action switch
        {
            "action.eat_meal" => "eat_loop",
            "action.drink" => "drink_kneel_loop",
            "action.sleep" => "sleep_loop",
            "action.gather_wood" => "chop_loop",
            "action.gather_food" => "forage_pick_loop",
            "action.tend_fire" or "action.warm_up" => "warm_hands_loop",
            "action.socialize" or "action.converse" => talk,
            "action.rest" => "sit_ground_loop",
            "action.flee" => "sprint",
            _ => id % 2 == 0 ? "idle" : "idle_alt",
        }, 1f);
    }
}
