using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// M1-18 play mode (scenarios with a player): the graybox camp on flat ground with the player's body (WASD, Shift to
/// run, third-person camera, wheel zoom), settlers drawn as the skinned stand-in character (walk/idle by speed, lying
/// down asleep, name and activity above the head) and conversation entry — [E] near a settler starts one, [Esc] leaves.
/// The dialogue panel's input and streaming are M1-19. `-- --view` keeps the old overhead camp view;
/// `-- --autotest-camp` walks to a settler, talks, leaves, and quits 0/1.
/// </summary>
public partial class SimHost
{
    private const string CharacterModel = "res://assets/characters/humanoid_a.glb";
    private const float WalkSpeed = 1.6f, RunSpeed = 3.6f, TalkRangeM = 4f;   // canon §10.9 walk; ConversationSystem.StartRangeM = 6 m
    private bool _play, _autotestCamp;
    private Vector2 _player;
    private Vector2 _lastReportedPlayer = new(float.NaN, float.NaN);
    private long _lastPlayerStep = -1;
    private Node3D? _playerBody;
    private AnimationPlayer? _playerAnim;
    private float _zoom = 1f;
    private PackedScene? _characterScene;
    private readonly Dictionary<ulong, (Node3D Body, AnimationPlayer? Anim, Label3D Label, Vector2 Last)> _people = [];
    private Dictionary<ulong, string> _names = [];
    private DialogueHost? _dialogue;
    private Label? _panel;
    private ulong _talkTarget;
    private int _campPhase, _dialoguePhase;
    private bool _autotestDialogue;
    private int _outcomesAtUnsay;
    private double _campT, _campMark;
    private ulong _campTarget;

    private void InitPlay(Vector2 player, FeudalSim.AI.AiConfig? config, Sim.Content.ContentDatabase content)
    {
        _play = true;
        _player = player;
        _settlers.Visible = false;   // the capsule multimesh is the overhead view's
        foreach (var label in GetChildren().OfType<Label3D>()) { (label.PixelSize, label.Position) = (0.012f, label.Position with { Y = 2.6f }); }   // place names, sized for the ground
        _characterScene = GD.Load<PackedScene>(CharacterModel);
        (_playerBody, _playerAnim) = Spawn();
        _playerBody.AddChild(new Label3D { Text = "you", Position = new Vector3(0, 2.15f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 48, OutlineSize = 12, PixelSize = 0.012f, Modulate = new Color(0.4f, 1f, 1f) });
        AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(240, 240) }, Position = new Vector3(10, -0.01f, -6), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.45f, 0.28f) } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, 35, 0), ShadowEnabled = true, LightEnergy = 1.0f });

        // The dialogue host: classification, DPs and lines for the player's conversation (the panel is M1-19's).
        if (config is not null && _aiStack is not null)
        {
            _dialogue = new DialogueHost(_runner!.Submit, _aiStack.Chat, _aiStack.Decider, config, content);
            _runner.Dialogue = _dialogue;
        }

        InitDialogue();

        _panel = new Label { Position = new Vector2(12, 300), Size = new Vector2(760, 220), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _panel.AddThemeColorOverride("font_color", new Color(1, 0.95f, 0.8f));
        _panel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _panel.AddThemeConstantOverride("outline_size", 6);
        GetNode<CanvasLayer>("Overlay").AddChild(_panel);
        GD.Print($"SimHost: play mode — player at {player}, characters {CharacterModel}");
    }

    private Task? _namesPending;

    /// <summary>Names live in the sim (state); fetched through the runner when people appear that the client hasn't named yet.</summary>
    private void RefreshNames(int count)
    {
        if (_namesPending is { IsCompleted: false } || _names.Count >= count) { return; }
        _namesPending = _runner!.Invoke(w =>
        {
            var names = new Dictionary<ulong, string>();
            for (var i = 0; i < w.People.Count; i++) { names[w.People.Ids[i].Value] = w.People.Names[i]; }
            return names;
        }).ContinueWith(t => { if (t.IsCompletedSuccessfully) { _names = t.Result; } }, TaskScheduler.Default);
    }

    private (Node3D Body, AnimationPlayer? Anim) Spawn()
    {
        var body = _characterScene!.Instantiate<Node3D>();
        AddChild(body);
        var anim = Find(body);
        foreach (var name in new[] { "walk", "idle" })
        {
            if (anim?.GetAnimation(name) is { } a) { a.LoopMode = Animation.LoopModeEnum.Linear; }
        }

        anim?.Play("idle");
        return (body, anim);

        static AnimationPlayer? Find(Node n)
        {
            if (n is AnimationPlayer p) { return p; }
            foreach (var c in n.GetChildren()) { if (Find(c) is { } f) { return f; } }
            return null;
        }
    }

    private static void Animate(AnimationPlayer? anim, bool moving, float speedScale)
    {
        if (anim is null) { return; }
        var want = moving ? "walk" : "idle";
        if (anim.CurrentAnimation != want) { anim.Play(want, 0.2); }
        anim.SpeedScale = moving ? speedScale : 1f;
    }

    /// <summary>Per frame in play mode: the player's body, the settlers' bodies, the talk hint and the conversation panel.</summary>
    private void UpdatePlay(RenderSnapshot snap, float delta)
    {
        // The player: input (or the camp autotest) → position; reported once per sim step when it moved.
        var input = Vector2.Zero;
        if (!_autotestCamp && _dialogue?.Conversation is null)
        {
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) { input.Y -= 1; }
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) { input.Y += 1; }
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) { input.X -= 1; }
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) { input.X += 1; }
        }
        else if (_autotestCamp && _campTarget != 0 && _people.TryGetValue(_campTarget, out var target) && _campPhase == 1)
        {
            var to = new Vector2(target.Body.Position.X, target.Body.Position.Z) - _player;
            if (to.Length() > 2.5f) { input = to.Normalized(); }
        }

        var speed = Input.IsKeyPressed(Key.Shift) || _autotestCamp ? RunSpeed : WalkSpeed;
        var moved = input.LengthSquared() > 0;
        if (moved) { _player += input.Normalized() * speed * delta; }
        _playerBody!.Position = new Vector3(_player.X, 0, _player.Y);
        if (moved) { _playerBody.Rotation = new Vector3(0, Mathf.Atan2(input.X, input.Y), 0); }
        Animate(_playerAnim, moved, speed / WalkSpeed);
        if (snap.Step != _lastPlayerStep && _runner!.Mode != RunMode.Paused && _player != _lastReportedPlayer)
        {
            _runner.Submit(CommandSource.Embodiment, new PlayerMoved(_player.X, _player.Y, _playerBody.Rotation.Y));
            (_lastPlayerStep, _lastReportedPlayer) = (snap.Step, _player);
        }

        RefreshNames(snap.Count);

        // Settlers: bodies at the client's LOD0 position (or the sim's), walking when they moved this frame.
        var seen = new HashSet<ulong>();
        ulong nearest = 0;
        var nearestD = TalkRangeM;
        for (var i = 0; i < snap.Count; i++)
        {
            if (snap.IsPlayer[i]) { continue; }
            var id = snap.Ids[i];
            seen.Add(id);
            if (!_people.TryGetValue(id, out var p))
            {
                var (body, anim) = Spawn();
                var label = new Label3D { Position = new Vector3(0, 2.1f, 0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 40, OutlineSize = 10, PixelSize = 0.01f };
                body.AddChild(label);
                p = (body, anim, label, new Vector2(snap.X[i], snap.Z[i]));
            }

            var at = _bodies.TryGetValue(id, out var b) ? b : new Vector2(snap.X[i], snap.Z[i]);
            var step = at - p.Last;
            var asleep = (snap.ActivityFlags[i] & Sim.World.ActivityState.Asleep) != 0;
            var walking = !asleep && delta > 0 && step.Length() / delta > 0.3f;
            p.Body.Position = new Vector3(at.X, asleep ? 0.15f : 0, at.Y);
            p.Body.Rotation = asleep ? new Vector3(-Mathf.Pi / 2, 0, 0) : new Vector3(0, walking ? Mathf.Atan2(step.X, step.Y) : -snap.Yaw[i], 0);
            Animate(p.Anim, walking, Math.Clamp(step.Length() / Math.Max(delta, 1e-3f) / WalkSpeed, 0.6f, 2.5f));
            var action = snap.Action[i];
            var doing = action >= 0 && ActivityLook.TryGetValue(_content!.Actions[action].Id, out var look) ? look.Label : "";
            var d = at.DistanceTo(_player);
            p.Label.Text = $"{_names.GetValueOrDefault(id, "…")}{(doing.Length > 0 ? $"\n{doing}" : "")}";
            p.Label.Visible = d < 25f;
            if (!asleep && d < nearestD) { (nearest, nearestD) = (id, d); }
            _people[id] = (p.Body, p.Anim, p.Label, at);
        }

        foreach (var gone in _people.Keys.Where(k => !seen.Contains(k)).ToList()) { _people[gone].Body.QueueFree(); _people.Remove(gone); }
        _talkTarget = nearest;

        // Camera: behind and above the player, north up; wheel zoom.
        _camera.Position = new Vector3(_player.X, 7.5f * _zoom, _player.Y + (10f * _zoom));
        _camera.LookAt(new Vector3(_player.X, 1.2f, _player.Y));

        // Conversation entry; the dialogue panel (M1-19) takes over while one is open.
        _panel!.Text = _dialogue?.Conversation is null && nearest != 0 ? $"[E] talk to {_names.GetValueOrDefault(nearest, "them")}   ·   [P] people" : "";
        UpdateDialogue(delta);
        if (_autotestCamp) { CampAutotest(delta); }
    }

    private void TryTalk()
    {
        if (_talkTarget == 0 || _dialogue?.Conversation is not null) { return; }
        _runner!.Submit(CommandSource.Player, new StartConversation(new EntityId(_talkTarget)));
        GD.Print($"SimHost: talk → {_names.GetValueOrDefault(_talkTarget, _talkTarget.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
    }

    private void Leave()
    {
        if (_dialogue?.Conversation is { } c) { _runner!.Submit(CommandSource.Player, new EndConversation(c.Id)); }
    }

    /// <summary>
    /// `--autotest-dialogue` (M1-19, after the camp autotest's approach): a typed request is echoed and answered with a
    /// streamed line; a typed insult waits in the confirm window and is unsaid — nothing reaches the sim; a quick-intent
    /// apology is answered; the People page lists the settler. True when done.
    /// </summary>
    private bool DialogueAutotest()
    {
        var npc = _dialogue?.Conversation?.NpcName ?? "?";
        bool Logged(string s) => _log.Any(l => l.Contains(s, StringComparison.Ordinal));
        var t = _campT - _campMark;
        switch (_dialoguePhase)
        {
            case 0:
                Expect("dialogue panel shown", _dlg?.Visible == true ? 1 : 0, 1, 0);
                Expect("header shows a demeanor cue", _dlgHeader?.Text.Contains("seems:", StringComparison.Ordinal) == true ? 1 : 0, 1, 0);
                SubmitText("Could you help me gather firewood for an hour?");
                (_dialoguePhase, _campMark) = (1, _campT);
                break;
            case 1 when (Logged($"[b]{npc}:[/b]") && _outcomes.Count > 0) || t > 12:   // line and outcome travel on different threads
                Expect("typed request echoed", Logged("read as: Request") ? 1 : 0, 1, 0);
                Expect("NPC line streamed in", Logged($"[b]{npc}:[/b]") ? 1 : 0, 1, 0);
                Expect("their choice surfaced (outcome)", _outcomes.Count, 1, null);
                (_dialoguePhase, _campMark) = (2, _campT);
                break;
            case 2 when t > 2.2:   // the 2 s turn limit
                SubmitText("You lazy fool.");
                (_dialoguePhase, _campMark) = (3, _campT);
                break;
            case 3 when _pending is not null || t > 5:
                Expect("insult held in the confirm window", _pending is { Consequential: true } ? 1 : 0, 1, 0);
                _outcomesAtUnsay = _outcomes.Count;
                Unsay();
                (_dialoguePhase, _campMark) = (4, _campT);
                break;
            case 4 when t > 3:
                Expect("unsaid: no choice was made", _outcomes.Count - _outcomesAtUnsay, 0, 0);
                Expect("unsaid noted", Logged("unsaid") ? 1 : 0, 1, 0);
                Act(new QuickIntent("apologize", "[You apologize]"));
                (_dialoguePhase, _campMark) = (5, _campT);
                break;
            case 5 when _outcomes.Count > _outcomesAtUnsay || t > 12:
                Expect("quick-intent apology answered", _outcomes.Count - _outcomesAtUnsay, 1, null);
                TogglePeople();
                (_dialoguePhase, _campMark) = (6, _campT);
                break;
            case 6 when _peoplePage?.Visible == true || t > 5:
                Expect("People page lists them", _peopleText?.Text.Contains(npc, StringComparison.Ordinal) == true ? 1 : 0, 1, 0);
                if (Array.IndexOf(OS.GetCmdlineUserArgs(), "--dialogue-shot") is var ds and >= 0 && ds + 1 < OS.GetCmdlineUserArgs().Length)
                {
                    GetViewport().GetTexture().GetImage().SavePng(OS.GetCmdlineUserArgs()[ds + 1]);
                }

                TogglePeople();
                _dialoguePhase = 7;
                return true;
        }

        return false;
    }

    /// <summary>`--autotest-camp`: bodies for every settler, walk up to an awake one, [E], a conversation opens, [Esc], it closes.</summary>
    private void CampAutotest(float delta)
    {
        if ((int)(_campT + delta) / 5 != (int)_campT / 5) { GD.Print($"SimHost: camp autotest t {_campT:F0} s phase {_campPhase} names {_names.Count} bodies {_people.Count} talk {_talkTarget} target {_campTarget} player {_player}"); }
        _campT += delta;
        switch (_campPhase)
        {
            case 0 when _campT > 2 && _names.Count > 0:
                Expect("a body per settler", _people.Count, _names.Count - 1, 0);
                _campTarget = _people.Where(kv => kv.Value.Body.Rotation.X == 0 && new Vector2(kv.Value.Body.Position.X, kv.Value.Body.Position.Z).DistanceTo(_player) > 8f).OrderBy(kv => new Vector2(kv.Value.Body.Position.X, kv.Value.Body.Position.Z).DistanceTo(_player)).Select(kv => kv.Key).FirstOrDefault();
                Expect("an awake settler to talk to", _campTarget != 0 ? 1 : 0, 1, 0);
                (_campPhase, _campMark) = (1, _campT);
                break;
            case 1 when _talkTarget == _campTarget:
                Expect("walked within talking range (s)", _campT - _campMark, 0, null);
                TryTalk();
                (_campPhase, _campMark) = (2, _campT);
                break;
            case 1 when _campT - _campMark > 30:
                Expect("walked within talking range", 0, 1, 0);
                _campPhase = 4;
                break;
            case 2 when _dialogue?.Conversation is not null || _campT - _campMark > 5:
                Expect("conversation opened with the target", _dialogue?.Conversation?.Npc.Value == _campTarget ? 1 : 0, 1, 0);
                if (_autotestDialogue) { (_campPhase, _campMark) = (7, _campT); break; }
                Leave();
                (_campPhase, _campMark) = (3, _campT);
                break;
            case 7 when DialogueAutotest():
                Leave();
                (_campPhase, _campMark) = (3, _campT);
                break;
            case 3 when _dialogue?.Conversation is null || _campT - _campMark > 5:
                Expect("conversation closed on [Esc]", _dialogue?.Conversation is null ? 1 : 0, 1, 0);
                _campPhase = 4;
                break;
            case 4:
                foreach (var line in _autotestResults) { GD.Print(line); }
                GD.Print($"SimHost: CAMP AUTOTEST {(_autotestFailed ? "FAIL" : "PASS")}");
                _campfire?.Stop();
                _uiPlayer?.Stop();
                _voicePlayer?.Stop();
                (_campPhase, _campMark) = (5, _campT);
                break;
            case 5 when _campT - _campMark > 0.25:
                _campPhase = 6;
                GetTree().Quit(_autotestFailed ? 1 : 0);
                break;
        }
    }
}
