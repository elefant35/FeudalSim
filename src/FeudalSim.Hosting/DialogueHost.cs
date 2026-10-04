using System.Collections.Concurrent;
using FeudalSim.AI;
using FeudalSim.AI.Dialogue;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Dialogue;
using FeudalSim.Sim.Events;

namespace FeudalSim.Hosting;

/// <summary>The player's open conversation as last seen on the sim thread (read by the UI and <see cref="DialogueHost.SayAsync"/>).</summary>
public sealed record ConversationView(ulong Id, int Turn, EntityId Npc, string NpcName, string PlayerName, IReadOnlyList<string> Transcript, IReadOnlyList<string> OthersPresent, string ActiveBusiness);

/// <summary>
/// Hosts the dialogue turn pipeline (22 §4) beside the sim: player text → rate limit → sanitize → classify →
/// <see cref="PlayerUtteranceClassified"/>; then, on the sim thread after each step, the NPC's LLM-decidable DPs for that turn
/// are bundled with prompt facts read from the world (<see cref="TurnBundleBuilder"/>) and handed to the
/// <see cref="DialogueReplyRouter"/>, whose decisions and lines come back as logged commands. Everything else (fast-decider
/// DPs, NPC↔NPC) stays with the <see cref="AiGateway"/>. The sim never waits for any of it.
/// </summary>
public sealed class DialogueHost
{
    private readonly Action<CommandSource, StateCommand> _submit;
    private readonly TurnClassifier _classifier;
    private readonly DialogueReplyRouter _router;
    private readonly TurnRateLimiter _limiter;
    private readonly ConcurrentDictionary<ulong, bool> _routed = new();
    private volatile ConversationView? _view;

    public DialogueHost(Action<CommandSource, StateCommand> submit, IChatProvider? chat, IDecider? decider, AiConfig config, ContentDatabase content, TimeProvider? clock = null)
    {
        _submit = submit;
        _classifier = new TurnClassifier(config.TemplateMode ? null : decider);
        _router = new DialogueReplyRouter(config.TemplateMode ? null : chat, config.TemplateMode ? null : decider, config, new TemplateBank(content));
        _limiter = new TurnRateLimiter(clock ?? TimeProvider.System);
        _router.Decided += d => _submit(CommandSource.Ai, d);
        _router.Line += l => _submit(CommandSource.Ai, l);
        _router.Partial += p => Partial?.Invoke(p);
        _router.Surfaced += s => Surfaced?.Invoke(s);
        _router.Spent += c => Spend(c);
        if (chat is OpenAiCompatibleChatProvider streamed) { streamed.StreamFinished += r => Spend(r.CostUsd); }
    }

    private double _spent;

    /// <summary>Dialogue spend this session: classification, replies, regenerations and verification (22 §12).</summary>
    public double SpentUsd => Volatile.Read(ref _spent);

    private void Spend(double usd)
    {
        double seen, next;
        do { seen = _spent; next = seen + usd; } while (Interlocked.CompareExchange(ref _spent, next, seen) != seen);
    }

    public event Action<PartialLine>? Partial;
    public event Action<DecisionSurfaced>? Surfaced;

    /// <summary>Raised when a classified turn has been submitted (UI: the intent echo, 22 §4.1).</summary>
    public event Action<Classification>? Classified;

    public ConversationView? Conversation => _view;

    /// <summary>
    /// The player says something in the open conversation. Waits for the rate limit (2 s per conversation), then classifies
    /// and submits the turn. Returns the classification, or null with no conversation open.
    /// </summary>
    public async Task<Classification?> SayAsync(string text, CancellationToken ct = default)
    {
        if (_view is not { } view) { return null; }
        var wait = _limiter.Check(view.Id);
        if (wait > TimeSpan.Zero) { await Task.Delay(wait, ct).ConfigureAwait(false); }
        _limiter.Record(view.Id);
        var line = Sanitizer.Clean(text);
        var context = new ClassifierContext(view.PlayerName, view.NpcName, view.OthersPresent, view.ActiveBusiness, view.Transcript);
        var c = await _classifier.ClassifyAsync(line, context, ct).ConfigureAwait(false);
        Spend(c.CostUsd);
        var (task, hours) = c.Act == "request" ? RequestTask(line.Text) : ("", 0f);
        _submit(CommandSource.Player, TurnClassifier.ToCommand(view.Id, view.Turn + 1, line, c, task, hours));
        _view = view with { Turn = view.Turn + 1 };   // the next line numbers on even before the sim catches up
        Classified?.Invoke(c);
        return c;
    }

    /// <summary>22 §4.5 for M1 favors: which camp task a request names (firewood, food, the fire) and for how many hours.</summary>
    public static (string Task, float Hours) RequestTask(string text)
    {
        var t = text.ToLowerInvariant();
        var task = t.Contains("wood", StringComparison.Ordinal) || t.Contains("logs", StringComparison.Ordinal) || t.Contains("kindling", StringComparison.Ordinal) ? "action.gather_wood"
            : t.Contains("food", StringComparison.Ordinal) || t.Contains("forag", StringComparison.Ordinal) || t.Contains("berries", StringComparison.Ordinal) || t.Contains("roots", StringComparison.Ordinal) ? "action.gather_food"
            : t.Contains("fire", StringComparison.Ordinal) ? "action.tend_fire" : "";
        var hours = 0f;
        var numbers = Extractor.Numbers(text);
        foreach (var n in numbers)
        {
            var after = text[(n.Start + n.Length)..].TrimStart();
            if (after.StartsWith("hour", StringComparison.OrdinalIgnoreCase)) { hours = n.Value; }
        }

        if (hours == 0f && t.Contains("an hour", StringComparison.Ordinal)) { hours = 1f; }
        return (task, hours);
    }

    /// <summary>
    /// Sim thread, after each step: publishes the conversation view, forwards the sim's resolutions to the router, and takes
    /// the LLM-decidable DPs of the player's conversation partner. Returns the DPs it took (the caller routes the rest).
    /// </summary>
    public HashSet<ulong> OnStep(SimWorld world, StepOutput output)
    {
        foreach (var e in output.Events)
        {
            if (e.Payload is DecisionResolved r && _routed.ContainsKey(r.Dp)) { _router.OnResolved(r.Dp, r.Chosen); _routed.TryRemove(r.Dp, out _); }
            if (e.Payload is DecisionPointCancelled x) { _router.OnResolved(x.Dp, ""); _routed.TryRemove(x.Dp, out _); }
        }

        var conv = world.Conversations.Open.FirstOrDefault(c => c.Player == world.PlayerId);
        _view = conv is null ? null : TurnBundleBuilder.View(world, conv);
        var taken = new HashSet<ulong>();
        if (conv is null) { return taken; }
        var mine = output.OpenedDecisions.Where(d => d.MaxDecider == DeciderKind.Llm && d.Context.Chooser == conv.Npc).ToList();
        if (mine.Count == 0)
        {
            // A turn the policy decided inline (injection flag, one-option menus): voice the response it chose.
            var inline = output.Events.Select(e => e.Payload).OfType<DecisionResolved>()
                .Where(r => r.Chooser == conv.Npc && r.Guard == GuardOutcome.Inline && r.Owner != Sim.Social.RapportOwner.Id).ToList();
            if (output.AppliedCommands.Any(c => c.Payload is PlayerUtteranceClassified u && u.Conversation == conv.Id) && inline.Count > 0
                && TurnBundleBuilder.Inline(world, conv, inline) is { } decided)
            {
                _ = Task.Run(() => _router.VoiceDecidedAsync(decided.Bundle, decided.Choice, CancellationToken.None));
            }

            return taken;
        }

        foreach (var d in mine) { taken.Add(d.Id); _routed[d.Id] = true; }
        var bundle = TurnBundleBuilder.Build(world, conv, mine);
        _ = Task.Run(() => _router.RunAsync(bundle, CancellationToken.None));
        return taken;
    }
}

/// <summary>Reads prompt facts and template slots from sim state for one NPC turn (sim thread only). M1-13 deepens the persona.</summary>
public static class TurnBundleBuilder
{
    /// <summary>Response DPs come first (the primary); then the initiative; rapport rides in its own header line.</summary>
    public static TurnBundle Build(SimWorld world, Conversation conv, IReadOnlyList<DecisionPointOpened> dps)
    {
        var initiative = dps.FirstOrDefault(d => d.Owner == InitiativeOwner.Id);
        var rapport = dps.FirstOrDefault(d => d.Owner == Sim.Social.RapportOwner.Id);
        var primary = dps.FirstOrDefault(d => d != initiative && d != rapport) ?? initiative ?? rapport ?? dps[0];
        var people = world.People;
        var npc = people.IndexOf(conv.Npc);
        var player = people.IndexOf(conv.Player);
        string npcName = npc >= 0 ? people.Names[npc] : "someone", playerName = player >= 0 ? people.Names[player] : "the stranger";
        var playerLine = conv.Transcript.LastOrDefault(l => l.StartsWith(playerName + ":", StringComparison.Ordinal)) is { } pl ? pl[(playerName.Length + 1)..].Trim() : "";
        var slots = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var dp in dps)
        {
            foreach (var o in dp.Options) { slots[o.Id] = Slots(world, o, npcName, playerName); }
        }

        return new TurnBundle(conv.Id, conv.Turn, conv.Npc, npcName, playerName, playerLine, conv.LastAct, primary,
            initiative ?? primary, rapport, PersonaFacts.For(world, npc, player, conv), slots, world.Decisions.PolicyChoiceOf(primary.Id));
    }

    /// <summary>A bundle for a turn already decided inline: synthetic one-option menus from content glosses (no DP went out).</summary>
    public static (TurnBundle Bundle, string Choice)? Inline(SimWorld world, Conversation conv, IReadOnlyList<DecisionResolved> resolved)
    {
        var response = resolved.FirstOrDefault(r => r.Owner != InitiativeOwner.Id) ?? resolved[0];
        DecisionPointOpened? Synthetic(DecisionResolved r)
        {
            var menu = world.Content.Decisions.FirstOrDefault(d => d.Owner == r.Owner && d.Options.Any(o => o.Id == r.Chosen));
            var def = menu?.Options.First(o => o.Id == r.Chosen);
            if (def is null) { return null; }
            var option = new MenuOption(r.Chosen, def.Family, [], true, 1f, 0f, def.Stakes, def.FavorsPlayer, def.Gloss);
            return new DecisionPointOpened(r.Dp, r.Owner, new DpContext("inline", r.Chooser, conv.Player, 0), 0, [option], [r.Chosen], world.Clock.Step, world.Clock.Step, DeciderKind.Policy);
        }

        if (Synthetic(response) is not { } primary) { return null; }
        var init = resolved.FirstOrDefault(r => r.Owner == InitiativeOwner.Id && r != response) is { } ir ? Synthetic(ir) : null;
        var bundle = Build(world, conv, init is null ? [primary] : [primary, init]);
        return (bundle with { Primary = primary, Initiative = init ?? primary }, response.Chosen);
    }

    public static ConversationView View(SimWorld world, Conversation conv)
    {
        var people = world.People;
        var npc = people.IndexOf(conv.Npc);
        var others = new List<string>();
        for (var k = 0; k < people.Count && others.Count < 6; k++)
        {
            if (k != npc && !world.IsPlayer(k) && npc >= 0 && Sim.Social.Escalation.Within(world, npc, k, 8f)) { others.Add(people.Names[k]); }
        }

        var business = world.Negotiations.Count > 0 ? "haggling" : "none";
        return new ConversationView(conv.Id, conv.Turn, conv.Npc, npc >= 0 ? people.Names[npc] : "?", world.PlayerRow >= 0 ? people.Names[world.PlayerRow] : "you",
            [.. conv.Transcript], others, business);
    }

    private static Dictionary<string, string> Slots(SimWorld world, MenuOption o, string npc, string player)
    {
        var s = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player, ["npc"] = npc };
        foreach (var p in o.Params)
        {
            switch (p.Key)
            {
                case "price_f": s["price"] = Sim.Economy.Money.Words(p.Value); s["price_f"] = p.Value.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                case "task" when p.Value >= 0 && p.Value < world.Content.Actions.Count: s["task"] = world.Content.Actions[(int)p.Value].Name.ToLowerInvariant(); break;
                case "claim" when p.Value >= 0 && p.Value < world.Claims.Count: s["claim"] = PersonaFacts.ClaimText(world, (int)p.Value); break;
                case "reason" when p.Value >= 0 && p.Value < world.Content.Actions.Count: s["reason"] = "I need to " + world.Content.Actions[(int)p.Value].Name.ToLowerInvariant(); break;
            }
        }

        if (world.Negotiations.Count > 0)
        {
            // M1 has at most one haggle at a time: its item names the {item} slot.
            var neg = world.Negotiations.Open.FirstOrDefault();
            if (neg is not null) { s["item"] = world.Content.Items[neg.Item].Name.ToLowerInvariant(); }
        }

        return s;
    }
}
