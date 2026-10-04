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
