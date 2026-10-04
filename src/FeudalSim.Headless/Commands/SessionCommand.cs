using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FeudalSim.AI;
using FeudalSim.AI.Dialogue;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Events;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class SessionSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/m1_talk.yaml";

    [CommandOption("--turns <N>")]
    [Description("Player turns in the session (22 §12.1: typical play-hour = 40).")]
    public int Turns { get; init; } = 40;

    [CommandOption("--per-npc <N>")]
    [Description("Turns with one settler before walking to the next.")]
    public int PerNpc { get; init; } = 5;

    [CommandOption("--max-usd <USD>")]
    public double MaxUsd { get; init; } = 0.25;

    [CommandOption("--turn-timeout <S>")]
    public double TurnTimeout { get; init; } = 12;
}

/// <summary>
/// M1-23 (22 §17.2 #1, #8): a scripted multi-settler session through the whole pipeline in real time — the gateway, the
/// dialogue host, the runner — with timing per beat against 22 §12.4 and spend per component; the per-turn and background
/// costs extrapolate to 22 §12.3's light / typical / heavy hours.
/// </summary>
public sealed class SessionCommand : Command<SessionSettings>
{
    private static readonly string[] Lines =
    [
        "Good evening. Long day?", "How are you finding the camp so far?", "Could you help me gather firewood for an hour tomorrow?",
        "What do you make of the captain's plans for the winter?", "That's a fine knife you carry. Did you make it?", "Thank you, that's kind of you.",
        "I heard someone's been taking more than their share of the food.", "Do you miss home?", "Would you sell me a bit of rope?",
        "You look tired. Sit by the fire a while.", "Who here do you trust most?", "I'm sorry if I was short with you earlier.",
        "The wind's turned cold tonight.", "What did you do before the voyage?", "You're slow as a wet week, you know that?",
        "Will you help me tend the fire tonight, two hours?", "Is there anything you need from the stores?", "Good night, then. Rest well.",
        "Did you see who was at the woodpile this morning?", "I promise I'll pay you back for the meal.",
    ];

    public override int Execute(CommandContext context, SessionSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var config = AiCli.LoadConfig(Console.Out);
        if (config.TemplateMode) { Console.WriteLine("session: template mode — nothing to measure"); return 1; }
        Console.WriteLine($"session: dialogue {config.DialogueModel} at {(config.ChatBaseUrl.Contains("127.0.0.1", StringComparison.Ordinal) || config.ChatBaseUrl.Contains("localhost", StringComparison.Ordinal) ? "local" : "cloud")} · decider {config.DeciderModel} · {settings.Turns} turns, {settings.PerNpc} per settler · cap ${settings.MaxUsd:F2}");
        using var jobs = new JobRunner(1);
        var world = scenario.CreateWorld(content, jobs);
        using var stack = AiStack.Create(config);
        using var gateway = stack.CreateGateway();
        using var runner = new SimRunner(world, null, RunMode.Running, gateway);
        var host = new DialogueHost(runner.Submit, stack.Chat, stack.Decider, config, content);
        runner.Dialogue = host;
        double classifySpend = 0;
        host.Classified += c => classifySpend += c.CostUsd;
        var clock = Stopwatch.StartNew();
        double turnAt = 0;
        double? gesture = null, firstWords = null, final = null;
        DialogueLineRendered? line = null;
        host.Surfaced += _ => gesture ??= clock.Elapsed.TotalMilliseconds - turnAt;
        host.Partial += p => { firstWords ??= clock.Elapsed.TotalMilliseconds - turnAt; if (p.Final) { final = clock.Elapsed.TotalMilliseconds - turnAt; } };
        host.Rendered += l => line = l;
        var ttfts = new System.Collections.Concurrent.ConcurrentBag<double>();
        host.FirstToken += ms => ttfts.Add(ms);

        Thread.Sleep(1_500);
        var rows = new List<(double Classify, double? Gesture, double? Words, double? Final, string Source, string Flags, string Act)>();
        var decided = new List<(string Decider, string Guard)>();
        var npcRow = 0;
        var startSpend = 0.0;
        var startWall = clock.Elapsed.TotalSeconds;
        for (var t = 0; t < settings.Turns; t++)
        {
            if (host.SpentUsd + gateway.SpentUsd - startSpend >= settings.MaxUsd) { Console.WriteLine("session: spend cap reached"); break; }
            if (host.Conversation is null || t % settings.PerNpc == 0)
            {
                if (host.Conversation is { } open) { runner.Submit(CommandSource.Player, new EndConversation(open.Id)); Thread.Sleep(300); }
                for (var tries = 0; tries < 24 && host.Conversation is null; tries++)
                {
                    npcRow = (npcRow % 24) + 1;
                    var (id, pos, asleep) = runner.Invoke(w => (w.People.Ids[npcRow], w.People.Transforms[npcRow], w.People.Activity[npcRow].Has(FeudalSim.Sim.World.ActivityState.Asleep))).GetAwaiter().GetResult();
                    if (asleep) { continue; }
                    runner.Submit(CommandSource.Embodiment, new PlayerMoved(pos.X + 1f, pos.Z, 0f));
                    Thread.Sleep(200);
                    runner.Submit(CommandSource.Player, new StartConversation(id));
                    for (var i = 0; i < 20 && host.Conversation is null; i++) { Thread.Sleep(100); }
                }

                if (host.Conversation is null) { Console.WriteLine("session: nobody to talk to"); break; }
            }

            (gesture, firstWords, final, line) = (null, null, null, null);
            turnAt = clock.Elapsed.TotalMilliseconds;
            var text = Lines[t % Lines.Length];
            var c = host.SayAsync(text, cancellationToken).GetAwaiter().GetResult();
            var classify = clock.Elapsed.TotalMilliseconds - turnAt;
            var deadline = clock.Elapsed.TotalSeconds + settings.TurnTimeout;
            while ((final is null || line is null) && clock.Elapsed.TotalSeconds < deadline) { Thread.Sleep(20); }   // a closing line still comes
            var ended = "";
            while (runner.Events.TryPop(out var e))
            {
                if (e.Payload is DecisionResolved r) { decided.Add((r.Decider.ToString(), r.Guard.ToString())); }
                if (e.Payload is ConversationEnded end) { ended = $" · ended: {end.Reason}"; }
            }
            rows.Add((classify, gesture, firstWords, final, line?.Source ?? "none", line?.Flags ?? "", c?.Act ?? "?"));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {t + 1,2} [{c?.Act,-14}] classify {classify,5:F0} · gesture {gesture,5:F0} · words {firstWords,5:F0} · line {final,5:F0} ms · {line?.Source} {line?.Flags}{ended} · breaker {host.ReplyBreaker} · \"{Trim(line?.Text)}\""));
            Thread.Sleep(2_100);   // the 2 s turn limit; also a player's reading time
        }

        var wall = clock.Elapsed.TotalSeconds - startWall;
        runner.Pause();
        static double P(IEnumerable<double?> xs, double q) { var v = xs.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToArray(); return v.Length == 0 ? double.NaN : v[Math.Min(v.Length - 1, (int)Math.Ceiling(q * v.Length) - 1)]; }
        string Pct(IEnumerable<double?> xs) => string.Create(CultureInfo.InvariantCulture, $"{P(xs, 0.5) / 1000,4:F2} / {P(xs, 0.95) / 1000,4:F2} s");
        var n = rows.Count;
        var turnSpend = host.SpentUsd;
        var background = gateway.SpentUsd;
        var perTurn = n == 0 ? 0 : turnSpend / n;
        var backgroundPerHour = background / wall * 3600;
        Console.WriteLine($"\nsession: {n} turns in {wall:F0} s wall · lines by source: {string.Join(", ", rows.GroupBy(r => r.Source).Select(g => $"{g.Key} {g.Count()}"))} · regenerated/flags: {rows.Count(r => r.Flags.Length > 0)}");
        Console.WriteLine($"  decisions: {decided.Count} · {string.Join(", ", decided.GroupBy(d => $"{d.Decider}/{d.Guard}").Select(g => $"{g.Key} {g.Count()}"))}");
        Console.WriteLine("22 §12.4 latency (p50 / p95), cloud targets in brackets:");
        Console.WriteLine($"  classification             {Pct(rows.Select(r => (double?)r.Classify))}   [≤ 0.3 / ≤ 0.7 s]");
        Console.WriteLine($"  LLM time to first token    {Pct(ttfts.Select(x => (double?)x))}   [cloud < 1.0 / < 2.0 s · local < 1.5 / < 3.0 s]");
        Console.WriteLine($"  decision gesture           {Pct(rows.Select(r => r.Gesture))}   [≤ 1.1 / ≤ 2.5 s]");
        Console.WriteLine($"  first words                {Pct(rows.Select(r => r.Words))}   [Tier A ≤ 1.2 / ≤ 3.0 s · Tier B ≤ 2.5 / ≤ 4.5 s]");
        Console.WriteLine($"  whole line                 {Pct(rows.Select(r => r.Final))}");
        var deadlineShare = decided.Count == 0 ? 0 : decided.Count(d => d.Guard == "Deadline") / (double)decided.Count;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  DP deadline expiries       {deadlineShare:P1}   [< 3%]"));
        Console.WriteLine("22 §12.3 cost:");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  per turn ${perTurn:F5} (classification ${(n == 0 ? 0 : classifySpend / n):F5} · reply + verification ${(n == 0 ? 0 : (turnSpend - classifySpend) / n):F5}) [model ≈ $0.00066]"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  background (overheard, barks …) ${background:F5} in {wall:F0} s → ${backgroundPerHour:F4} / wall-hour"));
        foreach (var (name, turns, target) in new[] { ("light", 15, 0.05), ("typical", 40, 0.05), ("heavy", 120, 0.10) })
        {
            var hour = (perTurn * turns) + backgroundPerHour;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {name,-8} {turns,3} turns/h → ${hour:F4} / play-hour   target ≤ ${target:F2}   {(hour <= target ? "PASS" : "FAIL")}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"session: spend ${turnSpend + background:F4}"));
        return 0;
    }

    private static string Trim(string? s) => s is null ? "" : s.Length > 70 ? s[..70] + "…" : s;
}
