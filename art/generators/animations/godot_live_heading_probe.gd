extends SceneTree
# Art-owned live-client regression evidence. Instantiates the actual Boot scene
# and reads existing M2 skeletons; it never supplies or alters character yaw.
# Key input exercises the real UpdatePlay path. The mean eye REST landmark,
# transformed by the live Skeleton3D, determines forward without assumed axes.
var boot: Node
var started_ms: int
var finishing := false
var previous := {}
var sampled_at := {}
var rows := []
var keys_held: Array = []
var output_path := "res://../art/previews/animations/live_heading_before.json"
var phases = [[KEY_W], [KEY_D], [KEY_S], [KEY_A], [KEY_W, KEY_D], [KEY_W, KEY_A], [KEY_S, KEY_D], [KEY_S, KEY_A]]
var source_hash := ""
var binary_hash := ""

func xyz(v: Vector3) -> Array:
    return [v.x, v.y, v.z]

func direction_name(v: Vector3) -> String:
    var sector = int(round(atan2(v.x, v.z) / (PI / 4)))
    return ["+Z", "+X/+Z", "+X", "+X/-Z", "-Z", "-X/-Z", "-X", "-X/+Z"][posmod(sector, 8)]

func set_keys(wanted: Array):
    for key in [KEY_W, KEY_A, KEY_S, KEY_D]:
        if (key in keys_held) == (key in wanted):
            continue
        var event = InputEventKey.new()
        event.keycode = key
        event.physical_keycode = key
        event.pressed = key in wanted
        Input.parse_input_event(event)
    keys_held = wanted.duplicate()

func _initialize():
    var args = OS.get_cmdline_user_args()
    var at = args.find("--art-heading-output")
    if at >= 0 and at + 1 < args.size():
        output_path = args[at + 1]
    source_hash = FileAccess.get_sha256("res://scripts/Bridge/SimHost.Play.cs")
    binary_hash = FileAccess.get_sha256("res://.godot/mono/temp/bin/Debug/FeudalSim.Game.dll")
    call_deferred("begin")

func begin():
    boot = load("res://scenes/boot/Boot.tscn").instantiate()
    root.add_child(boot)
    current_scene = boot
    started_ms = Time.get_ticks_msec()

func _process(_delta):
    if finishing or not is_instance_valid(boot) or started_ms == 0:
        return false
    var elapsed = (Time.get_ticks_msec() - started_ms) / 1000.0
    var phase = int((elapsed - 1.0) / 0.7)
    set_keys(phases[phase] if elapsed >= 1.0 and phase < phases.size() else [])
    # Called before Node process: pose and position both come from the same last
    # completed client frame. Each frame provides velocity; output is throttled.
    var now = Time.get_ticks_usec() / 1000000.0
    for skeleton in boot.find_children("*", "Skeleton3D", true, false):
        if skeleton.get_bone_count() != 40:
            continue
        var h = skeleton.find_bone("Head")
        var l = skeleton.find_bone("LeftEye")
        var r = skeleton.find_bone("RightEye")
        if h < 0 or l < 0 or r < 0:
            continue
        var body = skeleton
        while body.get_parent() != boot and body.get_parent() != null:
            body = body.get_parent()
        var role = "npc" if not body.find_children("*", "Label3D", true, false).is_empty() else "player"
        var identity = str(skeleton.get_path())
        var position: Vector3 = skeleton.global_position
        var last = previous.get(identity)
        previous[identity] = {"at": position, "time": now}
        if last == null or now - last.time < 0.00001:
            continue
        var velocity: Vector3 = (position - last.at) / (now - last.time)
        velocity.y = 0
        if velocity.length() < 0.5:
            continue
        # Reset snapshots/teleports are not locomotion.
        if velocity.length() > 8:
            continue
        if now - sampled_at.get(identity, -100.0) < (0.2 if role == "player" else 1.0):
            continue
        var head: Vector3 = skeleton.global_transform * skeleton.get_bone_global_rest(h).origin
        var eye: Vector3 = skeleton.global_transform * ((skeleton.get_bone_global_rest(l).origin + skeleton.get_bone_global_rest(r).origin) / 2)
        var forward = eye - head
        forward.y = 0
        forward = forward.normalized()
        var facing_dot = forward.dot(velocity.normalized())
        rows.append({"elapsed_s": elapsed, "role": role, "skeleton_path": identity, "body_path": str(body.get_path()), "position_world": xyz(position), "velocity_world_m_s": xyz(velocity), "direction": direction_name(velocity), "head_rest_world": xyz(head), "mean_eye_rest_world": xyz(eye), "forward_from_landmarks": xyz(forward), "forward_dot_velocity": facing_dot, "input_phase": phase})
        sampled_at[identity] = now
        if rows.size() >= 200:
            break
    if elapsed >= 7.2 or rows.size() >= 200:
        finish()
    return false

func finish():
    finishing = true
    set_keys([])
    var minimum = 1.0
    var maximum = -1.0
    var counts = {"player": 0, "npc": 0}
    var bodies = {}
    var directions = {"player": {}, "npc": {}}
    for row in rows:
        minimum = min(minimum, row.forward_dot_velocity)
        maximum = max(maximum, row.forward_dot_velocity)
        counts[row.role] += 1
        bodies[row.body_path] = row.role
        directions[row.role][row.direction] = directions[row.role].get(row.direction, 0) + 1
    var passed = not rows.is_empty() and counts.player >= 8 and counts.npc > 0 and minimum >= 0.95
    var command = "/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --max-fps 60 --path game --script ../art/generators/animations/godot_live_heading_probe.gd -- --scenario m1_view --template --art-heading-output " + output_path
    var report = {"command": command, "scenario": "m1_view (actual Boot scene and SimHost UpdatePlay)", "source_sha256_at_start": source_hash, "running_client_dll_sha256": binary_hash, "godot_version": Engine.get_version_info().string, "sample_count": rows.size(), "roles": counts, "body_count": bodies.size(), "bodies": bodies, "direction_counts": directions, "elapsed_s": (Time.get_ticks_msec() - started_ms) / 1000.0, "body_male_sha256": FileAccess.get_sha256("res://assets/characters/body_male.glb"), "body_female_sha256": FileAccess.get_sha256("res://assets/characters/body_female.glb"), "minimum_forward_dot_velocity": minimum, "maximum_forward_dot_velocity": maximum, "required_minimum": 0.95, "pass": passed, "samples": rows, "measurement": "Horizontal live Skeleton3D global position difference; horizontal mean(LeftEye,RightEye) rest world landmark minus Head rest world landmark. No assumed forward axis, no yaw writes. Synthetic WASD uses existing live player movement.", "shutdown": "Key release; Boot close notification stops audio; Boot _ExitTree disposes SimRunner and jobs; process exit code reflects audit."}
    var output = FileAccess.open(output_path, FileAccess.WRITE)
    if output:
        output.store_string(JSON.stringify(report, "  ") + "\n")
    else:
        printerr("ART_LIVE_HEADING could not write report: ", output_path)
        passed = false
    print("ART_LIVE_HEADING count=", rows.size(), " player=", counts.player, " npc=", counts.npc, " bodies=", bodies.size(), " min=", minimum, " max=", maximum, " pass=", passed, " report=", output_path)
    # Stop audio through the client's existing close path, then explicitly free
    # it so _ExitTree joins/disposes simulation threads before engine shutdown.
    boot.notification(Node.NOTIFICATION_WM_CLOSE_REQUEST)
    await create_timer(0.15, true, false, true).timeout
    boot.free()
    current_scene = null
    quit(0 if passed else 1)
