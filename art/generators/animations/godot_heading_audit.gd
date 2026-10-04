extends SceneTree
# Read-only audit of the imported M2 facing direction and bridge yaw mathematics.
# Run from repository root with the command recorded in the JSON output.

func xyz(v: Vector3) -> Array:
    return [v.x, v.y, v.z]

func _initialize():
    call_deferred("_audit")

func _audit():
    var document = GLTFDocument.new()
    var state = GLTFState.new()
    if document.append_from_file("res://assets/characters/body_male.glb", state) != OK:
        quit(1)
        return
    var body = document.generate_scene(state)
    root.add_child(body)
    var skeleton: Skeleton3D = body.find_children("*", "Skeleton3D", true, false)[0]
    var head = skeleton.get_bone_global_rest(skeleton.find_bone("Head")).origin
    var eye = skeleton.get_bone_global_rest(skeleton.find_bone("RightEye")).origin
    var forward = Vector3(0, 0, signf(eye.z - head.z))
    var play_path = "res://scripts/Bridge/SimHost.Play.cs"
    var source = FileAccess.get_file_as_string(play_path)
    var snippets = []
    var lines = source.split("\n")
    for i in lines.size():
        if "faceOffset" in lines[i] or "new PlayerMoved" in lines[i] or "walking ? Mathf.Atan2(step.X" in lines[i]:
            snippets.append({"line": i + 1, "text": lines[i].strip_edges()})
    var current_legacy_player = "const float faceOffset = Mathf.Pi;" in source
    var current_legacy_npc = "Mathf.Atan2(step.X, step.Y) + Mathf.Pi" in source
    var directions = {"north_plus_z": Vector3(0,0,1), "south_minus_z": Vector3(0,0,-1), "east_plus_x": Vector3(1,0,0), "west_minus_x": Vector3(-1,0,0), "north_east": Vector3(1,0,1), "north_west": Vector3(-1,0,1), "south_east": Vector3(1,0,-1), "south_west": Vector3(-1,0,-1)}
    var rows = []
    var ok = forward.z == 1
    for label in directions:
        var velocity: Vector3 = directions[label].normalized()
        var movement_yaw = atan2(velocity.x, velocity.z)
        var sim_yaw = atan2(velocity.x, -velocity.z)
        var corrected = Basis(Vector3.UP, movement_yaw) * forward
        var legacy = Basis(Vector3.UP, movement_yaw + PI) * forward
        var idle = Basis(Vector3.UP, PI - sim_yaw) * forward
        var legacy_idle = Basis(Vector3.UP, -sim_yaw) * forward
        rows.append({"direction": label, "velocity": xyz(velocity), "legacy_moving_heading": xyz(legacy), "corrected_moving_heading": xyz(corrected), "legacy_moving_dot_velocity": legacy.dot(velocity), "corrected_moving_dot_velocity": corrected.dot(velocity), "legacy_idle_dot_direction": legacy_idle.dot(velocity), "corrected_idle_dot_direction": idle.dot(velocity), "sim_yaw": sim_yaw, "correct_visual_yaw": movement_yaw})
        ok = ok and abs(corrected.dot(velocity) - 1) < 0.000001 and abs(idle.dot(velocity) - 1) < 0.000001 and abs(legacy.dot(velocity) + 1) < 0.000001 and abs(legacy_idle.dot(velocity) + 1) < 0.000001
    var camera_rows = []
    for i in 8:
        var camera_yaw = i * PI / 4
        var look = Vector3(-sin(camera_yaw), 0, -cos(camera_yaw))
        var corrected = Basis(Vector3.UP, camera_yaw + PI) * forward
        var legacy = Basis(Vector3.UP, camera_yaw + 2 * PI) * forward
        var reported_sim_yaw = atan2(look.x, -look.z)
        camera_rows.append({"camera_yaw": camera_yaw, "camera_look": xyz(look), "legacy_standing_dot_look": legacy.dot(look), "corrected_standing_dot_look": corrected.dot(look), "correct_sim_yaw": reported_sim_yaw})
        ok = ok and abs(corrected.dot(look) - 1) < 0.000001 and abs(legacy.dot(look) + 1) < 0.000001
    var report = {"command": "/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game --script ../art/generators/animations/godot_heading_audit.gd", "godot_version": Engine.get_version_info()["string"], "body_sha256": FileAccess.get_sha256("res://assets/characters/body_male.glb"), "body_forward": xyz(forward), "eye_minus_head_z": eye.z - head.z, "runtime_source": "game/scripts/Bridge/SimHost.Play.cs", "runtime_source_sha256": FileAccess.get_sha256(play_path), "runtime_source_snippets": snippets, "runtime_legacy_player_pi_offset_present": current_legacy_player, "runtime_legacy_npc_pi_offset_present": current_legacy_npc, "directions": rows, "camera_standing": camera_rows, "correct_formulas": {"player_moving_visual_yaw": "atan2(input.X, input.Y)", "npc_moving_visual_yaw": "atan2(step.X, step.Y)", "npc_idle_visual_yaw": "PI - snap.Yaw[i]", "player_standing_visual_yaw": "camera_yaw + PI", "player_reported_sim_yaw": "atan2(input.X, -input.Y)", "player_standing_sim_yaw": "-camera_yaw"}, "math_checks_pass": ok}
    var output = FileAccess.open("res://../art/previews/animations/heading_audit.json", FileAccess.WRITE)
    if not output:
        printerr("Could not persist heading audit")
        quit(1)
        return
    output.store_string(JSON.stringify(report, "  ") + "\n")
    print("ART_HEADING_AUDIT eight moving/idle directions and eight camera headings: corrected dot +1, legacy dot -1. Current legacy player=", current_legacy_player, " NPC=", current_legacy_npc, "; checks=", ok)
    body.queue_free()
    quit(0 if ok else 1)
