using System.Globalization;
using FeudalSim.AI;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

/// <summary>No options.</summary>
public sealed class NoSettings : CommandSettings;

/// <summary>Shared wiring for the AI CLI commands. Never prints a key — only whether one is set.</summary>
public static class AiCli
{
    /// <summary>Tests replace this to avoid reading the developer's real .env.</summary>
    internal static Func<AiConfig>? ConfigFactory { get; set; }

    public static AiConfig LoadConfig(TextWriter output)
    {
        var config = ConfigFactory?.Invoke() ?? AiConfig.Load(AiConfig.FindEnvFile(Directory.GetCurrentDirectory()));
        output.WriteLine($"key: {(config.ChatKey.IsSet ? "set" : "missing")}");
        return config;
    }
}

public sealed class AiPingCommand : AsyncCommand<NoSettings>
{
    internal static HttpMessageHandler? TestHandler { get; set; }

    public override Task<int> ExecuteAsync(CommandContext context, NoSettings settings, CancellationToken cancellationToken)
        => Run(Console.Out, cancellationToken);

    internal static async Task<int> Run(TextWriter output, CancellationToken ct)
    {
        var config = AiCli.LoadConfig(output);
        if (config.TemplateMode)
        {
            output.WriteLine("fallback: template");
            return 0;
        }

        using var stack = AiStack.Create(config, TestHandler);   // AI_GATEWAY_MODE applies (record / replay)
        try
        {
            var result = await stack.Chat!.CompleteAsync(new ChatRequest(config.DialogueModel,
                [new("system", "You are a settler in a medieval village game. Reply in one short sentence."),
                 new("user", "A stranger greets you on the beach the morning after the shipwreck. What do you say?")]), ct);
            output.WriteLine($"model: {config.DialogueModel} via {result.ProviderTag}");
            output.WriteLine($"reply: {result.Text}");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"latency: {result.LatencyMs} ms · tokens in/out: {result.TokensIn}/{result.TokensOut} · cost: ${result.CostUsd:F6}"));
            return 0;
        }
        catch (Exception ex) when (ex is AiProviderException or HttpRequestException or TaskCanceledException)
        {
            output.WriteLine($"chat: unavailable ({ex.Message}) → fallback: template");
            return 0;
        }
    }
}

public sealed class AiDecideCommand : AsyncCommand<NoSettings>
{
    internal static HttpMessageHandler? TestHandler { get; set; }

    public static readonly DecisionRequest Sample = new(
        "Hedda, a proud, hot-tempered fishwife; her opinion of the speaker is cold. The speaker says: \"Your fish stinks and so do you.\"",
        "Which response does Hedda choose?",
        ["laugh it off", "retort with an insult", "shove the speaker", "walk away"]);

    public override Task<int> ExecuteAsync(CommandContext context, NoSettings settings, CancellationToken cancellationToken)
        => Run(Console.Out, cancellationToken);

    internal static async Task<int> Run(TextWriter output, CancellationToken ct)
    {
        var config = AiCli.LoadConfig(output);
        DecisionResult result;
        if (config.TemplateMode || config.DeciderProvider is "heuristic")
        {
            output.WriteLine("fallback: heuristic");
            result = await new HeuristicDecider().DecideAsync(Sample, ct);
        }
        else
        {
            using var stack = AiStack.Create(config, TestHandler);   // AI_GATEWAY_MODE applies (record / replay)
            output.WriteLine($"gateway: {config.GatewayMode}");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Math.Max(config.DeciderTimeoutMs, 5_000));   // CLI ping allows a cold start
            result = await stack.Decider!.DecideAsync(Sample, cts.Token);
            if (!result.Ok)
            {
                output.WriteLine($"decider: unavailable ({result.Failure})");
                result = await new HeuristicDecider().DecideAsync(Sample, ct);
            }
        }

        output.WriteLine($"provider: {result.ProviderId}");
        for (var i = 0; i < Sample.Options.Count; i++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {(char)('A' + i)}) {Sample.Options[i],-24} {result.Probabilities[i]:F3}"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"sum: {result.Probabilities.Sum():F2} · chosen: {(char)('A' + result.Chosen)} · latency: {result.LatencyMs} ms · tokens in: {result.TokensIn} · cost: ${result.CostUsd:F6}"));
        return 0;
    }
}

public sealed class BenchDialogueSettings : CommandSettings
{
    [CommandOption("--models <IDS>")]
    [System.ComponentModel.Description("Comma-separated OpenRouter model ids.")]
    public string Models { get; init; } = "qwen/qwen3-14b";

    [CommandOption("--turns <N>")]
    public int Turns { get; init; } = 20;

    [CommandOption("--routing <LIST>")]
    [System.ComponentModel.Description("Comma-separated: default (OpenRouter's routing), latency, throughput.")]
    public string Routing { get; init; } = "default";

    [CommandOption("--prompt-dir <DIR>")]
    public string PromptDir { get; init; } = "tools/bench/s2";

    [CommandOption("--out <DIR>")]
    public string Out { get; init; } = "sim_runs";

    [CommandOption("--max-usd <USD>")]
    [System.ComponentModel.Description("Stop when the bench has spent this much (default 0.25).")]
    public double MaxUsd { get; init; } = 0.25;
}

/// <summary>Spike S2: dialogue latency and cost per model and routing (22 §12, §17.2 #1/#8). Never prints the key.</summary>
public sealed class AiBenchDialogueCommand : AsyncCommand<BenchDialogueSettings>
{
    private static readonly string[] OptionIds = ["accept_at_price", "counter_step_1", "counter_step_2", "defer_until_charcoal", "refuse"];
    private static readonly string[] RapportIds = ["warm_to_speaker", "stay_neutral", "cool_on_speaker"];

    public override async Task<int> ExecuteAsync(CommandContext context, BenchDialogueSettings settings, CancellationToken cancellationToken)
    {
        var config = AiCli.LoadConfig(Console.Out);
        if (config.TemplateMode) { Console.WriteLine("bench: needs a key (template mode)"); return 1; }
        var system = await File.ReadAllTextAsync(Path.Combine(settings.PromptDir, "system.txt"), cancellationToken);
        var userTemplate = await File.ReadAllTextAsync(Path.Combine(settings.PromptDir, "user.txt"), cancellationToken);
        var lines = (await File.ReadAllLinesAsync(Path.Combine(settings.PromptDir, "player_lines.txt"), cancellationToken)).Where(l => l.Trim().Length > 0).ToArray();
        using var stack = AiStack.Create(config);
        var turns = new List<BenchTurn>();
        var spent = 0.0;
        var stop = false;
        foreach (var model in settings.Models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var routing in settings.Routing.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (stop) { break; }
                var prefs = routing == "default" ? null : new System.Text.Json.Nodes.JsonObject { ["sort"] = routing };
                for (var t = 0; t < settings.Turns; t++)
                {
                    if (spent >= settings.MaxUsd)
                    {
                        Console.WriteLine($"bench: spend ceiling ${settings.MaxUsd:F2} reached — stopping");
                        stop = true;
                        break;
                    }

                    var user = userTemplate.Replace("{PLAYER_SAID}", lines[t % lines.Length], StringComparison.Ordinal);
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(20));
                    var r = await DialogueBench.RunTurnAsync(stack.Chat!, model, routing, t, system, user, OptionIds, RapportIds, prefs, cts.Token);
                    spent += r.CostUsd;
                    turns.Add(r);
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"  {model} [{routing}] #{t,2} {r.Provider,-12} ttft {r.FirstContentMs,6:F0} choice {r.ChoiceMs,6:F0} words {r.FirstWordsMs,6:F0} total {r.TotalMs,6:F0} ms · {r.TokensIn}/{r.TokensOut} tok · ${r.CostUsd:F6} · {(r.HeaderValid ? r.Choice : "INVALID")}{(r.Error is null ? "" : " · " + r.Error)}"));
                    await Task.Delay(500, cancellationToken);
                }
            }
        }

        Directory.CreateDirectory(settings.Out);
        var csv = Path.Combine(settings.Out, $"s2-dialogue-bench-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        var sb = new System.Text.StringBuilder("model,routing,turn,provider,first_token_ms,ttft_ms,choice_ms,first_words_ms,total_ms,tokens_in,tokens_out,cost_usd,choice,rapport,header_valid,think_leak,error,say\n");
        foreach (var r in turns)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Model},{r.Routing},{r.Turn},{r.Provider},{r.FirstTokenMs:F0},{r.FirstContentMs:F0},{r.ChoiceMs:F0},{r.FirstWordsMs:F0},{r.TotalMs:F0},{r.TokensIn},{r.TokensOut},{r.CostUsd:F7},{r.Choice},{r.Rapport},{r.HeaderValid},{r.ThinkLeak},\"{r.Error}\",\"{r.Say.Replace("\"", "'", StringComparison.Ordinal)}\"\n");
        }

        await File.WriteAllTextAsync(csv, sb.ToString(), cancellationToken);
        Console.WriteLine();
        Console.WriteLine("model / routing                               n  err  valid  ttft p50/p95   choice p50/p95  words p50/p95   total p50  in/out tok  $/turn     $/typical h  $/heavy h  think  providers");
        foreach (var g in turns.GroupBy(r => (r.Model, r.Routing)))
        {
            var ok = g.Where(r => r.Error is null).ToList();
            static double P(IEnumerable<double> xs, double q)
            {
                var a = xs.Where(x => x >= 0).OrderBy(x => x).ToArray();
                return a.Length == 0 ? double.NaN : a[Math.Min(a.Length - 1, (int)Math.Ceiling(q * a.Length) - 1)];
            }

            var perTurn = ok.Count == 0 ? double.NaN : ok.Average(r => r.CostUsd);
            // 22 §12.3 with the measured dialogue cost: per turn ×1.08 (regenerations) + $0.000345 fast-decider and
            // summary work; plus $0.0065 of per-hour items (openings, overheard talk, barks, rumors, gists, chronicle).
            double Hour(int turnsPerHour) => (turnsPerHour * ((1.08 * perTurn) + 0.000345)) + 0.0065;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(g.Key.Model + " / " + g.Key.Routing),-44} {g.Count(),2} {g.Count() - ok.Count,4} {(ok.Count == 0 ? 0 : 100.0 * ok.Count(r => r.HeaderValid) / ok.Count),5:F0}%  {P(ok.Select(r => r.FirstContentMs), 0.5),5:F0}/{P(ok.Select(r => r.FirstContentMs), 0.95),5:F0}    {P(ok.Select(r => r.ChoiceMs), 0.5),5:F0}/{P(ok.Select(r => r.ChoiceMs), 0.95),5:F0}    {P(ok.Select(r => r.FirstWordsMs), 0.5),5:F0}/{P(ok.Select(r => r.FirstWordsMs), 0.95),5:F0}    {P(ok.Select(r => r.TotalMs), 0.5),6:F0}  {(ok.Count == 0 ? 0 : ok.Average(r => r.TokensIn)),5:F0}/{(ok.Count == 0 ? 0 : ok.Average(r => r.TokensOut)),3:F0}  {perTurn:F6}   {Hour(40):F4}       {Hour(120):F4}    {ok.Count(r => r.ThinkLeak),3}  {string.Join("+", ok.Select(r => r.Provider).Distinct())}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bench: {turns.Count} turns, spent ${spent:F4}; wrote {csv}"));
        return 0;
    }
}
