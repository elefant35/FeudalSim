using System.Collections.Concurrent;
using FeudalSim.AI;
using FeudalSim.AI.Dialogue;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;

namespace FeudalSim.Hosting;

/// <summary>One golden goal of 22 §13.3 and how it went.</summary>
public sealed record GoalResult(string Goal, bool Done, int Attempts, string Detail);

public sealed record CompletabilityReport(IReadOnlyList<GoalResult> Goals, int Turns, int Lines, int TemplateLines, int Decisions, int PolicyDecisions, int Rejected, ulong FinalHash)
{
    public IReadOnlyList<string> RejectedReasons { get; init; } = [];

    /// <summary>Waits that ran out (0 in a healthy run: every routed DP was decided and every turn voiced).</summary>
    public int Timeouts { get; init; }

    public bool Passed => Goals.All(g => g.Done) && Decisions == PolicyDecisions && Lines >= Turns && TemplateLines == Lines;
}

/// <summary>
/// 22 §13.3 / §17.2 #12: the M1 golden scenario in template mode, through the real dialogue host (heuristic classifier,
/// claim extraction, policy for every DP, templates for every line), stepped deterministically on the calling thread.
/// Goals (M1 forms): get help with camp work and see the favor kept (a promise made and kept), barter and haggle, resolve
/// an insult with an apology, report a deed about someone and be believed. Each goal may take several settlers — the
/// policy says no often enough — but must succeed within the camp.
/// </summary>
public sealed class CompletabilityRun(ContentDatabase content, ScenarioDef scenario, TextWriter? log = null)
{
    private readonly ConcurrentQueue<(CommandSource, StateCommand)> _inbox = new();
    private readonly List<StepOutput> _outputs = [];
    private readonly HeadlessBodies _bodies = new();
    private readonly Dictionary<ulong, int> _lines = [];   // NPC lines the sim has applied, per conversation
    private readonly HashSet<ulong> _routed = [];
    private SimWorld _w = null!;
    private DialogueHost _host = null!;
    private int _turns, _timeouts;

    /// <summary>How long the driver waits for something the host owes (a busy machine can starve its thread pool).</summary>
    public const int WaitMs = 30_000;

    /// <summary>The 2 s turn limit runs on this clock: each read moves it 10 s on, so turns never wait.</summary>
    private sealed class FastClock : TimeProvider
    {
        private long _ticks = DateTimeOffset.UnixEpoch.Ticks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Add(ref _ticks, TimeSpan.TicksPerSecond * 10), TimeSpan.Zero);
    }

    public CompletabilityReport Run()
    {
        _w = scenario.CreateWorld(content, SerialJobScheduler.Instance);
        _host = new DialogueHost((s, c) => _inbox.Enqueue((s, c)), null, null, new AiConfig { LlmMode = "template" }, content, new FastClock());
        Steps(20);
        var npcs = Enumerable.Range(0, _w.People.Count).Where(r => r != _w.PlayerRow).ToList();
        var goals = new List<GoalResult> { Trade(npcs), Apology(npcs), Tell(npcs), Help(npcs) };   // help last: it runs the clock on
        var events = _outputs.SelectMany(o => o.Events).Select(e => e.Payload).ToList();
        var lines = _outputs.SelectMany(o => o.AppliedCommands).Select(c => c.Payload).OfType<DialogueLineRendered>().ToList();
        var resolved = events.OfType<DecisionResolved>().ToList();
        return new CompletabilityReport(goals, _turns, lines.Count, lines.Count(l => l.Source == "template"), resolved.Count, resolved.Count(r => r.Decider == DeciderKind.Policy),
            events.OfType<CommandRejected>().Count(), StateHasher.Hash(_w)) { RejectedReasons = [.. events.OfType<CommandRejected>().Select(r => r.Reason).Distinct()], Timeouts = _timeouts };
    }

    private GoalResult Help(List<int> npcs)
    {
        var attempts = 0;
        foreach (var npc in Awake(npcs))
        {
            attempts++;
            if (!Talk(npc)) { continue; }
            Say("Could you help me gather firewood for two hours?");
            var answer = Events<RequestAnswered>().LastOrDefault(a => a.Helper == _w.People.Ids[npc]);
            Leave();
            if (answer is null || answer.Answer is "refuse_request") { continue; }
            var helper = _w.People.Ids[npc];
            for (var i = 0; i < 2 * 18_000 && !Events<FavorDone>().Any(f => f.Doer == helper); i += 100) { Steps(100); }

            var done = Events<FavorDone>().Any(f => f.Doer == helper && f.For == _w.PlayerId);
            return new GoalResult("help: a favor asked, agreed and kept", done, attempts, $"{_w.People.Names[npc]} answered {answer.Answer}; favor {(done ? "done" : "not done in 2 days")}");
        }

        return new GoalResult("help: a favor asked, agreed and kept", false, attempts, "nobody agreed");
    }

    private GoalResult Trade(List<int> npcs)
    {
        var attempts = 0;
        foreach (var npc in Awake(npcs))
        {
            attempts++;
            Submit(CommandSource.Scenario, new SetHoldings(_w.People.Ids[npc], "item.iron_knife", 2, 200));
            Submit(CommandSource.Scenario, new SetHoldings(_w.PlayerId, "item.iron_axe", 0, 400));
            Steps(1);
            if (!Talk(npc)) { continue; }
            Submit(CommandSource.Player, new TradeOpen(_w.People.Ids[npc], "item.iron_knife", 1, PlayerSells: false));
            Steps(1);
            if (Events<TradeOffered>().LastOrDefault(o => o.Npc == _w.People.Ids[npc]) is not { } ask) { Leave(); continue; }
            Submit(CommandSource.Player, new TradeOffer(ask.Negotiation, (long)(ask.PriceF * 0.85)));   // haggle
            Settle(_host.Conversation?.Id ?? 0, _lines.GetValueOrDefault(_host.Conversation?.Id ?? 0) + 1);
            if (_w.Negotiations.Get(ask.Negotiation) is { NpcOffer: > 0 }) { Submit(CommandSource.Player, new TradeAccept(ask.Negotiation)); Steps(2); }
            var settled = Events<TradeSettled>().LastOrDefault(t => t.Negotiation == ask.Negotiation);
            Leave();
            if (settled is not null) { return new GoalResult("trade: barter and haggle", true, attempts, $"{_w.People.Names[npc]} asked {ask.PriceF}f, settled at {settled.PriceF}f"); }
        }

        return new GoalResult("trade: barter and haggle", false, attempts, "no deal");
    }

    private GoalResult Apology(List<int> npcs)
    {
        var attempts = 0;
        foreach (var npc in Awake(npcs))
        {
            attempts++;
            if (!Talk(npc)) { continue; }
            Say("You lazy fool.");
            if (_w.Conversations.Of(_w.People.Ids[npc]) is null)
            {
                // They walked off (or it came to blows): go after them a little later and try to make it right.
                Steps(300);
                if (!Talk(npc)) { continue; }
            }

            Say("I'm sorry. That was unkind of me and I was wrong to say it.");
            var answer = Events<DecisionResolved>().LastOrDefault(r => r.Owner == Sim.Social.ApologyOwner.Id && r.Chooser == _w.People.Ids[npc]);
            Leave();
            if (answer?.Chosen == "accept_apology") { return new GoalResult("insult: resolved with an apology", true, attempts, $"{_w.People.Names[npc]} accepted"); }
        }

        return new GoalResult("insult: resolved with an apology", false, attempts, "no apology accepted");
    }

    private GoalResult Tell(List<int> npcs)
    {
        var attempts = 0;
        var names = _w.People.Names;
        foreach (var npc in Awake(npcs))
        {
            attempts++;
            int a = npcs[(npcs.IndexOf(npc) + 3) % npcs.Count], b = npcs[(npcs.IndexOf(npc) + 5) % npcs.Count];
            if (!Talk(npc)) { continue; }
            Say($"I saw {names[a]} stole from {names[b]}.");
            var me = _w.People.Ids[npc];
            var answer = Events<DecisionResolved>().LastOrDefault(r => r.Owner == Sim.Social.BeingToldOwner.Id && r.Chooser == me);
            Leave();
            if (answer?.Chosen is "believe" or "repeat" or "keep_quiet") { return new GoalResult("tell: report a deed and be believed", true, attempts, $"{names[npc]} chose {answer.Chosen} ({names[a]} stole from {names[b]})"); }
        }

        return new GoalResult("tell: report a deed and be believed", false, attempts, "nobody believed it");
    }

    private IEnumerable<int> Awake(List<int> npcs) => npcs.Where(r => !_w.People.Activity[r].Has(Sim.World.ActivityState.Asleep));

    private bool Talk(int npc)
    {
        var t = _w.People.Transforms[npc];
        Submit(CommandSource.Embodiment, new PlayerMoved(t.X + 1f, t.Z, 0f));
        Steps(1);
        Submit(CommandSource.Player, new StartConversation(_w.People.Ids[npc]));
        for (var i = 0; i < 20 && _host.Conversation is null; i++) { Steps(1); }
        log?.WriteLine(_host.Conversation is null ? $"  {_w.People.Names[npc]}: no conversation" : $"  talking to {_w.People.Names[npc]}");
        return _host.Conversation is not null;
    }

    /// <summary>The player's line → classified, submitted, its DPs decided by the policy, the reply voiced by a template.</summary>
    private void Say(string text)
    {
        if (_host.Conversation is not { } view) { return; }
        var c = _host.SayAsync(text).GetAwaiter().GetResult();
        _turns++;
        var before = view.Transcript.Count;
        Settle(view.Id, _lines.GetValueOrDefault(view.Id) + 1);
        var conv = _w.Conversations.Get(view.Id);
        log?.WriteLine($"    > {text}  [{c?.Act}]");
        if (conv is not null && conv.Transcript.Count > before + 1) { log?.WriteLine($"    {conv.Transcript[^1]}"); }
        if (conv is null)
        {
            var why = Events<ConversationEnded>().LastOrDefault(e => e.Conversation == view.Id)?.Reason ?? "?";
            var chosen = string.Join(", ", Events<DecisionResolved>().Where(r => r.Chooser == view.Npc).TakeLast(3).Select(r => $"{r.Owner}:{r.Chosen}"));
            log?.WriteLine($"    (conversation ended: {why}; {chosen})");
        }
    }

    /// <summary>
    /// Steps until the turn is settled: every DP the host took is decided and the turn's line has been rendered (also when
    /// the answer ended the conversation). While the host owes the sim one of those it waits on the wall clock instead of
    /// stepping, so the step at which each submission lands — and so the whole run — never depends on thread timing.
    /// </summary>
    private void Settle(ulong conversation, int lines)
    {
        Steps(1);
        for (var i = 0; i < 400; i++)
        {
            var owed = _routed.Any(d => _w.Decisions.IsOpen(d)) || _lines.GetValueOrDefault(conversation) < lines;
            if (!owed && _inbox.IsEmpty) { Steps(2); return; }
            var start = Environment.TickCount64;
            while (owed && _inbox.IsEmpty && Environment.TickCount64 - start < WaitMs) { Thread.Sleep(1); }
            if (owed && _inbox.IsEmpty)
            {
                _timeouts++;
                Console.Error.WriteLine($"completability: wait timed out at step {_w.Clock.Step} — open routed DPs [{string.Join(",", _routed.Where(d => _w.Decisions.IsOpen(d)))}], lines {_lines.GetValueOrDefault(conversation)}/{lines}, conversation {(_w.Conversations.Get(conversation) is null ? "closed" : "open")}");
            }
            Steps(1);
        }
    }

    private void Leave()
    {
        if (_w.Conversations.Open.FirstOrDefault(c => c.Player == _w.PlayerId) is { } conv) { Submit(CommandSource.Player, new EndConversation(conv.Id)); }
        Steps(3);
    }

    private void Submit(CommandSource source, StateCommand c) => _inbox.Enqueue((source, c));

    private void Steps(int n)
    {
        for (var i = 0; i < n; i++)
        {
            while (_inbox.TryDequeue(out var c)) { _w.Enqueue(new CommandEnvelope(_w.LastCommandSeq + 1, 0, c.Item1, c.Item2)); }
            foreach (var r in _bodies.Step(_w)) { _w.Enqueue(new CommandEnvelope(_w.LastCommandSeq + 1, 0, CommandSource.Embodiment, r)); }
            var o = _w.Step();
            if (o.Events.Count > 0 || o.AppliedCommands.Count > 0) { _outputs.Add(o); }
            foreach (var c in o.AppliedCommands) { if (c.Payload is DialogueLineRendered l) { _lines[l.Conversation] = _lines.GetValueOrDefault(l.Conversation) + 1; } }
            foreach (var dp in o.OpenedDecisions) { _w.Enqueue(new CommandEnvelope(_w.LastCommandSeq + 1, 0, CommandSource.Integrity, dp)); }
            foreach (var d in _host.OnStep(_w, o)) { _routed.Add(d); }
            _routed.RemoveWhere(d => !_w.Decisions.IsOpen(d));
        }
    }

    private IEnumerable<T> Events<T>() where T : DomainEvent => _outputs.SelectMany(o => o.Events).Select(e => e.Payload).OfType<T>();
}
