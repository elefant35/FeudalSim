# Read-only contract orientation probe. Godot --headless --path game --script ../art/generators/animations/godot_forward_probe.gd
extends SceneTree

func read_scene(path: String) -> Node:
    var document = GLTFDocument.new()
    var state = GLTFState.new()
    var error = document.append_from_file(path, state)
    assert(error == OK, "GLB parse failed: " + path)
    var scene = document.generate_scene(state)
    root.add_child(scene)
    return scene

func world_rest(skeleton: Skeleton3D, bone: String) -> Vector3:
    return skeleton.global_transform * skeleton.get_bone_global_rest(skeleton.find_bone(bone)).origin

func world_pose(skeleton: Skeleton3D, bone: String) -> Vector3:
    return skeleton.global_transform * skeleton.get_bone_global_pose(skeleton.find_bone(bone)).origin

func coordinates(v: Vector3) -> Array:
    return [v.x, v.y, v.z]

func _initialize():
    call_deferred("_run_probe")

func _run_probe():
    var body = read_scene("res://assets/characters/body_male.glb")
    var skeleton: Skeleton3D = body.find_children("*", "Skeleton3D", true, false)[0]
    var head = world_rest(skeleton, "Head")
    var eye = world_rest(skeleton, "RightEye")
    var foot = world_rest(skeleton, "RightFoot")
    var toe = world_rest(skeleton, "RightToes")
    var head_mesh: MeshInstance3D = body.find_child("Head_male_a", true, false)
    var max_nose_z = -INF
    if head_mesh:
        for surface in head_mesh.mesh.get_surface_count():
            var arrays = head_mesh.mesh.surface_get_arrays(surface)
            for vertex in arrays[Mesh.ARRAY_VERTEX]:
                var point = head_mesh.global_transform * vertex
                max_nose_z = max(max_nose_z, point.z)
    var locomotion = read_scene("res://assets/characters/anims/locomotion.glb")
    var animated: Skeleton3D = locomotion.find_children("*", "Skeleton3D", true, false)[0]
    var player: AnimationPlayer = locomotion.find_children("*", "AnimationPlayer", true, false)[0]
    player.play("walk")
    player.seek(0.09, true)
    player.advance(0)
    var first = world_pose(animated, "LeftFoot")
    player.seek(0.27, true)
    player.advance(0)
    var second = world_pose(animated, "LeftFoot")
    var stance_z_m_s = (second.z - first.z) / 0.18
    var report = {"body_head_world": coordinates(head), "body_eye_world": coordinates(eye), "eye_forward_z_m": eye.z - head.z, "nose_max_world_z_m": max_nose_z, "toe_forward_z_m": toe.z - foot.z, "walk_stance_left_foot_start": coordinates(first), "walk_stance_left_foot_end": coordinates(second), "walk_stance_z_velocity_m_s": stance_z_m_s, "expected_actor_move_z_m_s": 1.6}
    print("ART_FORWARD_PROBE ", JSON.stringify(report))
    if not (eye.z > head.z and toe.z > foot.z and max_nose_z > head.z and abs(stance_z_m_s + 1.6) < 0.02):
        printerr("ART_FORWARD_PROBE orientation/stance check failed")
        quit(1)
        return
    report["godot_version"] = Engine.get_version_info()["string"]
    report["command"] = "/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game --script ../art/generators/animations/godot_forward_probe.gd"
    report["body_sha256"] = FileAccess.get_sha256("res://assets/characters/body_male.glb")
    report["locomotion_sha256"] = FileAccess.get_sha256("res://assets/characters/anims/locomotion.glb")
    var output = FileAccess.open("res://../art/previews/animations/forward_probe.json", FileAccess.WRITE)
    if output:
        output.store_string(JSON.stringify(report, "  ") + "\n")
    else:
        printerr("Could not persist forward probe report")
        quit(1)
        return
    body.queue_free()
    locomotion.queue_free()
    quit(0)
