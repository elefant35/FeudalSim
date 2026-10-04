using System.Collections.Concurrent;
using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using Godot;

namespace FeudalSim.Game.Bridge;

/// <summary>
/// M1-19 dialogue UI (19 §6): header with demeanor cue and last-exchange glyph, transcript with intent echoes and
/// gestures, streamed NPC lines (typewriter ≥ 25 chars/s), quick intents, free text with the 1.5 s confirm/unsay window
/// for consequential acts, the latency choreography (take at 0.3 s, filler after 1.5 s without words), proposal cards,
/// a stub trade panel and a basic People page ([P]). It sees only what the player may: the chosen option's gesture,
/// stance, glyph and fixed terms — never a menu or a propensity.
/// </summary>
public partial class SimHost
{
    private static readonly Dictionary<string, string> GestureWords = new()
    {
        ["nods"] = "nods", ["shakes_head"] = "shakes their head", ["considers"] = "considers it", ["raised_brow"] = "raises a brow",
        ["warm_smile"] = "smiles warmly", ["cold_look"] = "gives you a cold look", ["looks_away"] = "looks away", ["turns_away"] = "turns away",
        ["scowls"] = "scowls", ["squares_up"] = "squares up", ["shoves"] = "shoves you", ["swings"] = "swings at you", ["laughs"] = "laughs",
        ["palms_up"] = "shows open palms", ["shouts"] = "shouts for help", ["steps_between"] = "steps between", ["listening"] = "listens",
        ["leans_in"] = "leans in", ["beckons"] = "beckons", ["points"] = "points at you", ["hand_out"] = "holds out a hand",
        ["hand_out_for_coin"] = "holds out a palm for coin", ["crosses_arms"] = "crosses their arms", ["shrugs"] = "shrugs",
        ["wide_eyes"] = "goes wide-eyed", ["baffled"] = "looks baffled", ["draws_weapon"] = "reaches for a weapon",
    };

    private static readonly string[] Fillers = ["Hm.", "(rubs the back of their neck)", "(a slow breath)", "Well…"];
    private readonly ConcurrentQueue<Action> _ui = new();
    private PanelContainer? _dlg;
    private Label? _dlgHeader;
    private RichTextLabel? _dlgLog;
    private LineEdit? _dlgInput;
    private HBoxContainer? _proposal;
    private Label? _proposalText;
    private VBoxContainer? _trade;
    private Label? _tradeText;
    private PanelContainer? _peoplePage;
    private Label? _peopleText;
    private readonly List<string> _log = [];
    private string _streaming = "", _streamTarget = "";
    private double _streamChars;
    private PendingTurn? _pending;
    private double _pendingAt, _turnAt = -1;
    private bool _gestureShown, _wordsShown, _takeShown, _fillerShown;
    private ulong _dlgConversation;
    private string _glyph = "";
    private ulong _negotiation;
    private long _npcOfferF, _myOfferF;
    private readonly Dictionary<ulong, string> _lastCue = [];
    private readonly List<TurnOutcome> _outcomes = [];

    private void InitDialogue()
    {
        if (_dialogue is null) { return; }
        _dialogue.Surfaced += s => _ui.Enqueue(() => { _gestureShown = true; Note($"[i]{_dialogue.Conversation?.NpcName ?? "They"} {GestureWords.GetValueOrDefault(s.GestureTag, s.GestureTag.Replace('_', ' '))}.[/i]"); });
        _dialogue.Partial += p => _ui.Enqueue(() =>
        {
            _wordsShown = true;
            _streamTarget = p.Text;
            if (p.Final)
            {
                _log.Add($"[b]{_dialogue.Conversation?.NpcName ?? "They"}:[/b] {p.Text}");
                (_streaming, _streamTarget, _streamChars) = ("", "", 0);
            }
        });
        _dialogue.Outcome += o => _ui.Enqueue(() => OnOutcome(o));

        _dlg = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1f, AnchorBottom = 1f, OffsetLeft = -470, OffsetRight = 470, OffsetTop = -360, OffsetBottom = -12, Visible = false };
        var box = new VBoxContainer();
        _dlg.AddChild(box);
        _dlgHeader = new Label();
        box.AddChild(_dlgHeader);
        _dlgLog = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, CustomMinimumSize = new Vector2(920, 190), FitContent = false };
        box.AddChild(_dlgLog);
        _proposal = new HBoxContainer { Visible = false };
        _proposalText = new Label();
        _proposal.AddChild(_proposalText);
        _proposal.AddChild(Button("Accept", () => Act(new QuickIntent("accept_offer", "[You agree]"))));
        _proposal.AddChild(Button("Decline", () => Act(new QuickIntent("reject_offer", "[You decline]"))));
        box.AddChild(_proposal);
        _trade = new VBoxContainer { Visible = false };
        _tradeText = new Label();
        _trade.AddChild(_tradeText);
        var tradeRow = new HBoxContainer();
        tradeRow.AddChild(Button("Coin −", () => { _myOfferF = Math.Max(1, _myOfferF - 1); ShowTrade(); }));
        tradeRow.AddChild(Button("Coin +", () => { _myOfferF++; ShowTrade(); }));
        tradeRow.AddChild(Button("Offer", () => { if (_negotiation != 0) { _runner!.Submit(CommandSource.Player, new TradeOffer(_negotiation, _myOfferF)); Note($"[i]You offer {_myOfferF}f.[/i]"); } }));
        tradeRow.AddChild(Button("Confirm deal", () => { if (_negotiation != 0 && _npcOfferF > 0) { _runner!.Submit(CommandSource.Player, new TradeAccept(_negotiation)); } }));
        tradeRow.AddChild(Button("Walk away", () => { if (_negotiation != 0) { _runner!.Submit(CommandSource.Player, new TradeWalkAway(_negotiation)); } _trade.Visible = false; }));
        _trade.AddChild(tradeRow);
        box.AddChild(_trade);

        var intents = new HBoxContainer();
        intents.AddChild(Button("Ask", () => Act(new QuickIntent("ask", "[You ask how they are faring]"))));
        var request = new MenuButton { Text = "Request ▾", Flat = false };
        request.GetPopup().AddItem("a hand with firewood (1 h)", 0);
        request.GetPopup().AddItem("help foraging (2 h)", 1);
        request.GetPopup().AddItem("a turn at the fire (1 h)", 2);
        request.GetPopup().IdPressed += id => Act(id switch
        {
            0 => new QuickIntent("request", "[You ask for a hand with the firewood, an hour]", "action.gather_wood", 1f),
            1 => new QuickIntent("request", "[You ask for help foraging, two hours]", "action.gather_food", 2f),
            _ => new QuickIntent("request", "[You ask them to tend the fire for an hour]", "action.tend_fire", 1f),
        });
        intents.AddChild(request);
        intents.AddChild(Button("Trade", OpenTrade));
        intents.AddChild(Button("Compliment", () => Act(new QuickIntent("praise", "[You compliment their work]"))));
        intents.AddChild(Button("Apologize", () => Act(new QuickIntent("apologize", "[You apologize]"))));
        intents.AddChild(Button("Insult", () => Act(new QuickIntent("insult", "[You insult them]", Severity: 3))));
        intents.AddChild(Button("Threaten", () => Act(new QuickIntent("threaten", "[You threaten them]", Severity: 3))));
        intents.AddChild(Button("Leave", Leave));
        box.AddChild(intents);
        _dlgInput = new LineEdit { PlaceholderText = "Say something…  Enter say · Esc leave", CustomMinimumSize = new Vector2(920, 0) };
        _dlgInput.TextSubmitted += t => { _dlgInput.Text = ""; SubmitText(t); };
        _dlgInput.GuiInput += e =>
        {
            if (e is not InputEventKey { Pressed: true } k) { return; }
            if (k.Keycode == Key.Escape) { Leave(); _dlgInput.AcceptEvent(); }
            else if (k.Keycode == Key.Backspace && _pending is not null && _dlgInput.Text.Length == 0) { Unsay(); _dlgInput.AcceptEvent(); }
            else if (k.Keycode is Key.Enter or Key.KpEnter && _pending is not null && _dlgInput.Text.Length == 0) { Confirm(); _dlgInput.AcceptEvent(); }
        };
        box.AddChild(_dlgInput);
        GetNode<CanvasLayer>("Overlay").AddChild(_dlg);

        _peoplePage = new PanelContainer { Position = new Vector2(12, 120), Size = new Vector2(440, 420), Visible = false };
        _peopleText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        _peoplePage.AddChild(_peopleText);
        GetNode<CanvasLayer>("Overlay").AddChild(_peoplePage);
    }

    private static Button Button(string text, Action pressed)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }

    private void Note(string line) => _log.Add(line);

    /// <summary>Typed words: classify (the echo), then say — at once, or after the confirm window for consequential acts.</summary>
    private void SubmitText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || _dialogue?.Conversation is null || _pending is not null) { return; }
        _log.Add($"[b]You:[/b] {text}");
        _ = _dialogue.ReadAsync(text).ContinueWith(t => _ui.Enqueue(() => Read(t.IsCompletedSuccessfully ? t.Result : null)), TaskScheduler.Default);
    }

    private void Act(QuickIntent intent)
    {
        if (_dialogue?.Intent(intent) is not { } pending || _pending is not null) { return; }
        _log.Add($"[b]You:[/b] {intent.Shown}");
        Read(pending);
    }

    private void Read(PendingTurn? pending)
    {
        if (pending is null) { return; }
        if (pending.Consequential)
        {
            (_pending, _pendingAt) = (pending, _clock);
            _log.Add($"   ↳ {pending.Echo}   [color=#ffd27a][Enter] confirm · [Backspace] unsay[/color]");
            return;
        }

        _log.Add($"   ↳ {pending.Echo}");
        Say(pending);
    }

    private void Confirm()
    {
        if (_pending is not { } p) { return; }
        _pending = null;
        Say(p);
    }

    /// <summary>19 §6.3: an unsaid line never reached the sim — no DP opened, nobody heard it.</summary>
    private void Unsay()
    {
        if (_pending is null) { return; }
        _pending = null;
        _log.Add("   ↳ [s]unsaid[/s]");
    }

    private void Say(PendingTurn p)
    {
        if (_dialogue is null || !_dialogue.Commit(p)) { return; }
        (_turnAt, _gestureShown, _wordsShown, _takeShown, _fillerShown, _glyph) = (_clock, false, false, false, false, "");
    }

    private void OnOutcome(TurnOutcome o)
    {
        _outcomes.Add(o);
        if (o.Glyph is { } g) { _glyph = g; }
        if (o.Stance is { } s && _dialogue?.Conversation is { } c) { _lastCue[c.Npc.Value] = s; }
        if (o.Proposal is { } terms && _proposal is not null) { (_proposal.Visible, _proposalText!.Text) = (true, $"{_dialogue?.Conversation?.NpcName ?? "They"} proposes: {terms}   "); }
        else if (o.Owner != Sim.Dialogue.InitiativeOwner.Id && _proposal is not null) { _proposal.Visible = false; }
        if (o.Chosen is "retort" or "threaten") { Note("[color=#ff9a7a][i]Voices are raised.[/i][/color]"); }
        if (o.Chosen is "shove" or "attack_brawl") { Note("[color=#ff6a5a][b]It comes to blows.[/b][/color]"); }
    }

    /// <summary>The stub trade panel (19 §7.1): buy one of their goods if they have any (M1 has no other source of goods).</summary>
    private void OpenTrade()
    {
        if (_dialogue?.Conversation is not { } c) { return; }
        _ = _runner!.Invoke(w =>
        {
            for (var item = 0; item < w.Content.Items.Count; item++)
            {
                if (w.Holdings.Goods(c.Npc, item) > 0) { return w.Content.Items[item].Id; }
            }

            return null;
        }).ContinueWith(t => _ui.Enqueue(() =>
        {
            if (t.Result is not { } item) { Note("[i]They have nothing to trade yet.[/i]"); return; }
            _runner!.Submit(CommandSource.Player, new TradeOpen(c.Npc, item, 1, PlayerSells: false));
            (_trade!.Visible, _negotiation, _npcOfferF, _myOfferF) = (true, 0, 0, 0);
            ShowTrade();
        }), TaskScheduler.Default);
    }

    private void ShowTrade() => _tradeText!.Text = _negotiation == 0 ? "Trading… waiting for their ask." : $"Their price: {(_npcOfferF > 0 ? Sim.Economy.Money.Words(_npcOfferF) : "—")}   ·   your offer: {_myOfferF}f";

    /// <summary>Trade events from the sim (drained with the others): their asks and counters, the deal, the end.</summary>
    private void OnTradeEvent(Sim.Events.DomainEvent e)
    {
        switch (e)
        {
            case Sim.Events.TradeOffered o when _trade is { Visible: true }:
                (_negotiation, _npcOfferF) = (o.Negotiation, o.PriceF);
                if (_myOfferF == 0) { _myOfferF = Math.Max(1, o.PriceF * 3 / 4); }
                Note($"[i]{_dialogue?.Conversation?.NpcName ?? "They"} {(o.Move == "ask" ? "asks" : "counters with")} {Sim.Economy.Money.Words(o.PriceF)}.[/i]");
                ShowTrade();
                break;
            case Sim.Events.TradeSettled s when s.Negotiation == _negotiation:
                Note($"[color=#9aff9a]Deal: {Sim.Economy.Money.Words(s.PriceF)}.[/color]");
                (_trade!.Visible, _negotiation) = (false, 0);
                break;
            case Sim.Events.NegotiationEnded n when n.Negotiation == _negotiation:
                Note($"[i]The haggling ends ({n.Reason.Replace('_', ' ')}).[/i]");
                (_trade!.Visible, _negotiation) = (false, 0);
                break;
        }
    }

    /// <summary>Per frame: queued UI work, the confirm window, the choreography beats, the typewriter, the panels.</summary>
    private void UpdateDialogue(float delta)
    {
        while (_ui.TryDequeue(out var a)) { a(); }
        if (_dlg is null) { return; }
        var conv = _dialogue?.Conversation;
        if (conv is null)
        {
            if (_dlg.Visible) { (_dlg.Visible, _pending, _trade!.Visible, _proposal!.Visible) = (false, null, false, false); _dlgInput!.ReleaseFocus(); }
            return;
        }

        if (!_dlg.Visible || _dlgConversation != conv.Id)
        {
            (_dlg.Visible, _dlgConversation) = (true, conv.Id);
            _log.Clear();
            _log.Add($"[i]You approach {conv.NpcName}.[/i]");
            _dlgInput!.GrabFocus();
        }

        if (_pending is not null && _clock - _pendingAt >= 1.5) { Confirm(); }   // 19 §6.3: auto-confirm after 1.5 s
        if (_turnAt >= 0)
        {
            var t = _clock - _turnAt;
            if (!_takeShown && !_gestureShown && t >= 0.3) { _takeShown = true; Note($"[i]{conv.NpcName} takes it in.[/i]"); }
            if (!_fillerShown && !_wordsShown && t >= 1.5) { _fillerShown = true; Note($"[i]{Fillers[(int)(conv.Turn % Fillers.Length)]}[/i]"); }
            if (_wordsShown && _streamTarget.Length == 0) { _turnAt = -1; }
        }

        // Typewriter: at least 25 characters a second, catching up when tokens run ahead.
        if (_streamTarget.Length > _streaming.Length)
        {
            _streamChars += Math.Max(25.0, (_streamTarget.Length - _streaming.Length) * 4.0) * delta;
            _streaming = _streamTarget[..Math.Min(_streamTarget.Length, (int)_streamChars)];
        }

        _lastCue.TryGetValue(conv.Npc.Value, out var stance);
        _dlgHeader!.Text = $"{conv.NpcName} — {conv.Who}    seems: {conv.Cue}{(stance is null ? "" : $" · {stance}")}    {_glyph}";
        _dlgLog!.Text = string.Join("\n", _log.TakeLast(40)) + (_streaming.Length > 0 ? $"\n[b]{conv.NpcName}:[/b] {_streaming}▌" : "");
    }

    /// <summary>[P] the People page (19 §7.3, basic): who you have met, how well you know them, how they seemed, what you believe.</summary>
    private void TogglePeople()
    {
        if (_peoplePage is null) { return; }
        if (_peoplePage.Visible) { _peoplePage.Visible = false; return; }
        var cues = new Dictionary<ulong, string>(_lastCue);
        _ = _runner!.Invoke(w =>
        {
            var lines = new List<string> { "PEOPLE YOU HAVE MET   [P] close" };
            var me = w.PlayerId;
            for (var i = 0; i < w.People.Count; i++)
            {
                var id = w.People.Ids[i];
                if (id == me) { continue; }
                var f = w.Relationships.Familiarity(me, id);
                if (f < 1f && !cues.ContainsKey(id.Value)) { continue; }
                var p = w.People.Personality[i];
                var profession = p.Profession < w.Content.Professions.Count ? w.Content.Professions[p.Profession].Name.ToLowerInvariant() : "settler";
                lines.Add($"{w.People.Names[i]} — {profession} · {PersonaFacts.FamiliarityWords(f).Replace("knows", "you know", StringComparison.Ordinal)}{(cues.TryGetValue(id.Value, out var c) ? $" · last seemed {c}" : "")}");
                foreach (var b in w.Beliefs.Span(me).ToArray().Where(b => b.C >= Sim.Social.BeliefStore.Hold && (w.Claims[b.Claim].Subject == id.Value || w.Claims[b.Claim].Object == id.Value)).Take(2))
                {
                    lines.Add($"    you believe: {PersonaFacts.ClaimText(w, b.Claim)}{(b.FirstHand ? " (saw it)" : " (heard)")}");
                }
            }

            return lines.Count == 1 ? lines.Append("(nobody yet)").ToList() : lines;
        }).ContinueWith(t => _ui.Enqueue(() => { _peopleText!.Text = string.Join("\n", t.Result); _peoplePage.Visible = true; }), TaskScheduler.Default);
    }
}
