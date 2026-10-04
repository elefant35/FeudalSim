# Runtime GLTFDocument smoke check. Godot --headless --path game --script ../art/generators/animations/godot_probe.gd [-- --combat]
extends SceneTree

func _initialize():
    var report = {}
    var families = ["locomotion", "rest", "social", "needs", "work"]
    if "--combat" in OS.get_cmdline_user_args():
        families.append("combat_basic")
    for family in families:
        var doc = GLTFDocument.new()
        var state = GLTFState.new()
        var path = "res://assets/characters/anims/" + family + ".glb"
        var error = doc.append_from_file(path, state)
        if error != OK:
            printerr("ART_PROBE parse failed: ", path, " ", error)
            quit(1)
            return
        var scene = doc.generate_scene(state)
        var skeletons = scene.find_children("*", "Skeleton3D", true, false)
        var players = scene.find_children("*", "AnimationPlayer", true, false)
        var bones = []
        var animations = []
        for skeleton in skeletons:
            bones.append(skeleton.get_bone_count())
        for player in players:
            animations.append_array(player.get_animation_list())
        if bones != [40] or animations.is_empty():
            printerr("ART_PROBE invalid skeleton/animations: ", family)
            quit(1)
            return
        var sample = players[0].get_animation(animations[0])
        report[family] = {"skeleton_bones": bones, "clips": animations, "sample_track_path": str(sample.track_get_path(0))}
        scene.free()
    print("ART_PROBE ", JSON.stringify(report))
    quit(0)
