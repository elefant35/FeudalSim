using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.AI.Dialogue;

/// <summary>
/// The decision-first reply route (22 §4.1–4.12, canon §13.5 #2). One streamed generation per NPC turn decides the turn's
/// DPs and voices the choice:
/// <list type="number">
/// <item>Header: as soon as each header line completes it is checked (syntax, on the pre-cleared list) and submitted as a
/// <see cref="DecisionMade"/> (decider Llm); the choice's gesture goes to the UI (<see cref="DecisionSurfaced"/>). A bad or
/// missing header, prose first, or no CHOICE within the 4 s decision deadline → "policy, now" for that DP.</item>
/// <item>Speech: Tier A (low/medium stakes) streams sentence by sentence, each through the rule checks; a failing sentence
/// truncates the stream. Tier B (a high or critical option chosen or offered) is buffered, rule-checked and verified by the
/// fast decider (22 §4.9); on failure it is regenerated once speak-only, then a template speaks.</item>
/// <item>A policy-decided turn waits for the sim's resolution (<see cref="OnResolved"/>) and is voiced speak-only, or by a
/// template past the ~6 s speech cutoff or without a model.</item>
/// </list>
/// The line returns to the sim as a logged <see cref="DialogueLineRendered"/>. The router never decides anything itself.
/// </summary>
public sealed class DialogueReplyRouter(IChatProvider? chat, IDecider? verifier, AiConfig config, TemplateBank templates)
{
    public static readonly TimeSpan DecisionDeadline = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan SpeechCutoff = TimeSpan.FromSeconds(6);
    public static readonly TimeSpan ResolutionWait = TimeSpan.FromSeconds(8);

    /// <summary>22 §3.5: live dialogue fails over when no token arrives within 3 s.</summary>
    public static readonly TimeSpan TtftTimeout = TimeSpan.FromSeconds(3);   // default; config.TtftTimeoutMs overrides

    /// <summary>The TTFT target for the breaker's latency trigger (22 §17.2 #1: p50 &lt; 1.0 s).</summary>
    public const double TtftTargetMs = 1_000;

    /// <summary>The dialogue model's breaker: an open breaker sends the turn to the policy at once and a template speaks (22 §3.5).</summary>
    /// <summary>Milliseconds from the start of the reply route to the decision-first stream's first token (TTFT, 22 §12.4).</summary>
    public event Action<double>? FirstToken;

    public CircuitBreaker Breaker { get; } = new($"{chat?.Tag ?? "none"}:{config.DialogueModel}");

    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<string>> _resolved = new();

    public event Action<DecisionMade>? Decided;
    public event Action<DialogueLineRendered>? Line;
    public event Action<DecisionSurfaced>? Surfaced;
    public event Action<PartialLine>? Partial;

    /// <summary>Billed cost of the router's own non-streamed calls (speak-only regenerations, verification).</summary>
    public event Action<double>? Spent;

    /// <summary>The host forwards the sim's DecisionResolved for DPs this router handled (the policy's pick, a guard's override).</summary>
    public void OnResolved(ulong dp, string chosen) => Waiter(dp).TrySetResult(chosen);

    private TaskCompletionSource<string> Waiter(ulong dp) => _resolved.GetOrAdd(dp, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));

    public async Task RunAsync(TurnBundle b, CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var dps = new List<DecisionPointOpened> { b.Primary };
        if (b.Initiative is { } init && init.Id != b.Primary.Id) { dps.Add(init); }
        if (b.Rapport is { } rap) { dps.Add(rap); }
        foreach (var dp in dps) { Waiter(dp.Id); }

        if (chat is null || !Breaker.TryAcquire())
        {
            foreach (var dp in dps) { Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, null, DeciderKind.Policy, "template", 0, null)); }
            await VoiceResolvedAsync(b, started, null, "template", ct, chat is null ? null : ["breaker_open"]).ConfigureAwait(false);
            return;
        }

        var firstToken = -1.0;

        var submitted = new HashSet<ulong>();
        var header = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? primaryChoice = null;
        var say = new StringBuilder();
        var shown = new StringBuilder();
        var flags = new List<string>();
        var tierB = TierB(b.Primary, null);
        var headerFailed = false;
        var truncated = false;
        var buffer = new StringBuilder();
        var consumed = 0;   // characters of `say` already released or rejected
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(SpeechCutoff);
        try
        {
            var request = new ChatRequest(config.DialogueModel, PromptBuilder.DecisionFirst(b), MaxTokens: 140, Temperature: b.Facts.Temperature,
                Stop: ["<player_said", $"{b.PlayerName}:"]);   // 22 §4.8 (not "\n\n": S2)
            using var ttft = new CancellationTokenSource(config.TtftTimeoutMs > 0 ? TimeSpan.FromMilliseconds(config.TtftTimeoutMs) : TtftTimeout);   // LLM_TIMEOUT_TTFT_MS
            using var streamCut = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, ttft.Token);
            await foreach (var piece in chat.StreamAsync(request, streamCut.Token).ConfigureAwait(false))
            {
                if (firstToken < 0)
                {
                    firstToken = started.Elapsed.TotalMilliseconds;
                    ttft.CancelAfter(Timeout.InfiniteTimeSpan);   // the first token came: no TTFT cut any more
                    Breaker.RecordLatency(firstToken, TtftTargetMs);
                    FirstToken?.Invoke(firstToken);
                }

                buffer.Append(piece);
                if (primaryChoice is null && !headerFailed && started.Elapsed > DecisionDeadline) { headerFailed = true; flags.Add("header_deadline"); break; }

                // Header lines, as they complete.
                while (!header.ContainsKey("SAY"))
                {
                    var text = buffer.ToString();
                    var lead = text.TrimStart();
                    if (header.Count > 0 && lead.StartsWith("SAY:", StringComparison.Ordinal))
                    {
                        // The speech line needs no newline: everything after "SAY:" is speech as it arrives.
                        buffer.Clear().Append(lead[4..].TrimStart());
                        header["SAY"] = "";
                        break;
                    }

                    var nl = text.IndexOf('\n');
                    if (nl < 0) { break; }
                    var line = OpenAiCompatibleChatProvider.StripThink(text[..nl]).Trim();
                    buffer.Remove(0, nl + 1);
                    if (line.Length == 0) { continue; }
                    if (header.Count == 0 && !line.StartsWith("CHOICE:", StringComparison.Ordinal)) { headerFailed = true; flags.Add("prose_first"); break; }
                    if (line.StartsWith("SAY:", StringComparison.Ordinal)) { buffer.Insert(0, line[4..].TrimStart() + " "); header["SAY"] = ""; break; }
                    var colon = line.IndexOf(':');
                    if (colon < 0) { continue; }
                    var key = line[..colon].Trim();
                    var value = line[(colon + 1)..].Trim();
                    header[key] = value;
                    var dp = key switch { "CHOICE" => b.Primary, "INITIATIVE" => dps.FirstOrDefault(d => d == b.Initiative && d != b.Primary), "RAPPORT" => b.Rapport, _ => null };
                    if (dp is null || submitted.Contains(dp.Id)) { continue; }
                    var id = value == "none" && dp == b.Initiative ? "none" : value;
                    submitted.Add(dp.Id);
                    if (dp.PreCleared.Contains(id))
                    {
                        Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, id, DeciderKind.Llm, $"{chat.Tag}:{config.DialogueModel}", (int)started.ElapsedMilliseconds, null));
                        if (dp == b.Primary)
                        {
                            primaryChoice = id;
                            tierB = TierB(b.Primary, id);
                            Surfaced?.Invoke(new DecisionSurfaced(b.Conversation, b.Turn, b.Npc, templates.Gesture(id), null));
                        }
                    }
                    else
                    {
                        Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, null, DeciderKind.Policy, $"guard:off_menu:{Clip(value)}", (int)started.ElapsedMilliseconds, null));
                        flags.Add($"{key.ToLowerInvariant()}_off_menu");
                        if (dp == b.Primary) { headerFailed = true; }
                    }
                }

                if (headerFailed) { break; }
                if (primaryChoice is null || !header.ContainsKey("SAY")) { continue; }

                // Speech. Tier A: release complete sentences that pass the rule checks; Tier B: keep buffering.
                say.Append(buffer);
                buffer.Clear();
                while (!tierB && !truncated && SpeechChecks.NextSentenceEnd(say.ToString(), consumed) is var end and > 0)
                {
                    var sentence = say.ToString()[consumed..end];
                    consumed = end;
                    var (ok, failure) = SpeechChecks.Check(sentence, b.Facts.MaxWords, Allowed(b, primaryChoice), b.Facts.KnownNames);
                    if (ok is null) { truncated = true; flags.Add($"truncated:{failure}"); break; }
                    shown.Append(shown.Length > 0 ? " " : "").Append(ok);
                    Partial?.Invoke(new PartialLine(b.Conversation, b.Turn, b.Npc, shown.ToString(), false));
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or AiProviderException or HttpRequestException or System.Text.Json.JsonException)
        {
            flags.Add(ex is OperationCanceledException ? firstToken < 0 ? "ttft_timeout" : "cutoff" : "provider_error");
            if (firstToken < 0 || ex is not OperationCanceledException) { Breaker.RecordFailure(); }
        }

        if (firstToken >= 0 && !flags.Contains("provider_error")) { Breaker.RecordSuccess(); }

        // DPs the header never decided go to the policy now.
        foreach (var dp in dps.Where(d => !submitted.Contains(d.Id)))
        {
            Decided?.Invoke(new DecisionMade(dp.Id, dp.MenuHash, null, DeciderKind.Policy, headerFailed ? "guard:header" : "header_missing", (int)started.ElapsedMilliseconds, null));
        }

        if (primaryChoice is null || headerFailed)
        {
            await VoiceResolvedAsync(b, started, null, "regenerated", ct, flags).ConfigureAwait(false);
            return;
        }

        // The rest of the generated speech (a final sentence without a full stop, or a Tier B line).
        say.Append(buffer);
        var full = OpenAiCompatibleChatProvider.StripThink(say.ToString()).Trim();
        if (!tierB)
        {
            var rawSay = say.ToString();
            if (!truncated && rawSay.Length > consumed && rawSay[consumed..].Trim().Length > 0)
            {
                var rest = rawSay[consumed..];
                var (okRest, tailFailure) = SpeechChecks.Check(rest, b.Facts.MaxWords, Allowed(b, primaryChoice), b.Facts.KnownNames);
                if (okRest is not null) { shown.Append(shown.Length > 0 ? " " : "").Append(okRest); }
                else { flags.Add($"truncated:{tailFailure}"); }
            }

            var text = shown.ToString().Trim();
            if (text.Length > 0 && SpeechChecks.CarriesPrice(text, b.Slots.GetValueOrDefault(primaryChoice))) { Finish(b, text, "llm", flags); return; }
            if (text.Length > 0) { flags.Add("must_say:price"); await VoiceResolvedAsync(b, started, primaryChoice, "regenerated", ct, flags).ConfigureAwait(false); return; }
            await VoiceResolvedAsync(b, started, primaryChoice, "template", ct, flags).ConfigureAwait(false);
            return;
        }

        // Tier B: rule checks, then the fast decider's verification; one speak-only regeneration; then a template.
        var (checkedText, fail) = SpeechChecks.Check(full, b.Facts.MaxWords, Allowed(b, primaryChoice), b.Facts.KnownNames);
        if (checkedText is not null && !SpeechChecks.CarriesPrice(checkedText, b.Slots.GetValueOrDefault(primaryChoice))) { (checkedText, fail) = (null, "must_say:price"); }
        if (checkedText is not null && await VerifyAsync(b, primaryChoice, checkedText, ct).ConfigureAwait(false) is null)
        {
            Finish(b, checkedText, "llm", flags);
            return;
        }

        flags.Add(fail is null ? "verify_failed" : $"check:{fail}");
        await VoiceResolvedAsync(b, started, primaryChoice, "regenerated", ct, flags).ConfigureAwait(false);
    }

    /// <summary>
    /// A turn the policy decided inline (injection ≥ 0.3, template mode): no menu went out, but the choice still needs
    /// words — speak-only within the cutoff, else a template (22 §4.12).
    /// </summary>
    public Task VoiceDecidedAsync(TurnBundle b, string choice, CancellationToken ct)
    {
        Surfaced?.Invoke(new DecisionSurfaced(b.Conversation, b.Turn, b.Npc, templates.Gesture(choice), null));
        return VoiceResolvedAsync(b, Stopwatch.StartNew(), choice, chat is null ? "template" : "regenerated", ct, ["policy_turn"]);
    }

    /// <summary>Voices an already-decided choice: speak-only within the cutoff (rule checks, Tier B verification), else a template.</summary>
    private async Task VoiceResolvedAsync(TurnBundle b, Stopwatch started, string? known, string source, CancellationToken ct, List<string>? flags = null)
    {
        flags ??= [];
        var choice = known;
        if (choice is null)
        {
            var w = Waiter(b.Primary.Id).Task;
            choice = await Task.WhenAny(w, Task.Delay(ResolutionWait, ct)).ConfigureAwait(false) == w ? w.Result : null;
        }

        if (known is null && choice is { Length: > 0 }) { Surfaced?.Invoke(new DecisionSurfaced(b.Conversation, b.Turn, b.Npc, templates.Gesture(choice), null)); }
        var initiativeChoice = b.Initiative is { } init && init.Id != b.Primary.Id && Waiter(init.Id).Task.IsCompletedSuccessfully ? Waiter(init.Id).Task.Result : null;
        if (choice is null || choice.Length == 0) { Finish(b, templates.Render($"reply.{b.Act}", b.Primary.Id, null), "template", [.. flags, "unresolved"]); return; }
        var option = b.Primary.Options.FirstOrDefault(o => o.Id == choice);
        if (chat is not null && source != "template" && option is not null && started.Elapsed < SpeechCutoff)
        {
            using var cut = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cut.CancelAfter(SpeechCutoff - started.Elapsed);
            try
            {
                var initiative = initiativeChoice is null ? null : b.Initiative!.Options.FirstOrDefault(o => o.Id == initiativeChoice);
                var reply = await chat.CompleteAsync(new ChatRequest(config.DialogueModel, PromptBuilder.SpeakOnly(b, option, initiative), MaxTokens: 100, Temperature: b.Facts.Temperature), cut.Token).ConfigureAwait(false);
                Spent?.Invoke(reply.CostUsd);
                var text = reply.Text.Trim();
                var at = text.IndexOf("SAY:", StringComparison.Ordinal);
                if (at >= 0) { text = text[(at + 4)..].Trim(); }
                var (ok, fail) = SpeechChecks.Check(text, b.Facts.MaxWords, Allowed(b, choice), b.Facts.KnownNames);
                if (ok is not null && !SpeechChecks.CarriesPrice(ok, b.Slots.GetValueOrDefault(choice))) { (ok, fail) = (null, "must_say:price"); }
                if (ok is not null && (!TierB(b.Primary, choice) || await VerifyAsync(b, choice, ok, cut.Token).ConfigureAwait(false) is null))
                {
                    Finish(b, ok, "regenerated", flags);
                    return;
                }

                flags.Add(ok is null ? $"regen_check:{fail}" : "regen_verify_failed");
            }
            catch (Exception ex) when (ex is OperationCanceledException or AiProviderException or HttpRequestException or System.Text.Json.JsonException)
            {
                flags.Add(ex is OperationCanceledException ? "regen_cutoff" : "regen_error");
            }
        }

        Finish(b, Template(b, choice, initiativeChoice), "template", flags);
    }

    private string Template(TurnBundle b, string choice, string? initiative)
    {
        var main = choice == "none" && b.Primary == b.Initiative ? templates.Render($"reply.{b.Act}", b.Primary.Id, null)
            : templates.Render(choice, b.Primary.Id, b.Slots.GetValueOrDefault(choice));
        if (initiative is null or "none") { return main; }
        return main + " " + templates.Render(initiative, b.Initiative!.Id, b.Slots.GetValueOrDefault(initiative));
    }

    private void Finish(TurnBundle b, string text, string source, List<string> flags)
    {
        Partial?.Invoke(new PartialLine(b.Conversation, b.Turn, b.Npc, text, true));
        Line?.Invoke(new DialogueLineRendered(b.Conversation, b.Turn, b.Npc, text, source, string.Join(',', flags)));
    }

    /// <summary>22 §4.2: Tier B when the chosen option is high or critical stakes, or the menu offered one that was not chosen.</summary>
    public static bool TierB(DecisionPointOpened dp, string? chosen)
    {
        var offeredHigh = dp.Options.Any(o => dp.PreCleared.Contains(o.Id) && o.Stakes >= Stakes.High);
        return offeredHigh || (chosen is not null && dp.Options.FirstOrDefault(o => o.Id == chosen)?.Stakes >= Stakes.High);
    }

    /// <summary>
    /// 22 §4.9 fast-decider verification (Tier B): does the reply contradict the decision, express another option, agree to
    /// more, or step out of the world? Null when it passes (or no verifier is configured).
    /// </summary>
    public async Task<string?> VerifyAsync(TurnBundle b, string chosen, string reply, CancellationToken ct)
    {
        if (verifier is null) { return null; }
        var option = b.Primary.Options.FirstOrDefault(o => o.Id == chosen);
        if (option is null) { return "unknown_choice"; }
        var others = string.Join("; ", b.Primary.Options.Where(o => o.Id != chosen && b.Primary.PreCleared.Contains(o.Id)).Select(o => o.Gloss));
        var state = $"Decision: {b.NpcName} will {option.Gloss}.\nOther choices not made: {others}\n<reply>{reply}</reply>";
        (string Id, string Q, float Threshold)[] qs =
        [
            ("v_contradicts", $"Does the reply contradict or soften the decision \"{option.Gloss}\"?", 0.5f),
            ("v_other_option", "Does the reply instead express one of the other choices?", 0.4f),
            ("v_unapproved", "Does the reply agree to, give, or promise anything beyond the decision?", 0.4f),
            ("v_meta", "Does the reply mention anything outside a medieval world or talk about itself as a character or AI?", 0.5f),
        ];
        var results = await Task.WhenAll(qs.Select(async q => (q.Id, q.Threshold, R: await verifier.DecideAsync(new DecisionRequest(state, q.Q, ["yes", "no"]), ct).ConfigureAwait(false)))).ConfigureAwait(false);
        Spent?.Invoke(results.Sum(r => r.R.CostUsd));
        foreach (var (id, threshold, r) in results)
        {
            if (r.Ok && r.Probabilities[0] >= threshold) { return id; }
        }

        return null;
    }

    private static HashSet<long> Allowed(TurnBundle b, string? chosen)
    {
        var facts = new List<string>();
        if (chosen is not null && b.Slots.TryGetValue(chosen, out var slots)) { facts.AddRange(slots.Values); }
        if (chosen is not null && b.Primary.Options.FirstOrDefault(o => o.Id == chosen) is { } o) { facts.Add(o.Gloss); }
        facts.AddRange(b.Facts.Knows);
        return SpeechChecks.AllowedNumbers(facts, b.PlayerLine);
    }

    private static string Clip(string s) => s.Length > 24 ? s[..24] : s;
}
