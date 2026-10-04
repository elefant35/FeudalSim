using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace FeudalSim.AI;

/// <summary>One streamed decision-first dialogue turn, timed (spike S2, 22 §12.4). Times are ms from sending.</summary>
public sealed record BenchTurn(
    string Model, string Routing, int Turn, string Provider,
    double FirstTokenMs, double FirstContentMs, double ChoiceMs, double FirstWordsMs, double TotalMs,
    int TokensIn, int TokensOut, double CostUsd, string? Choice, string? Rapport, bool HeaderValid, bool ThinkLeak,
    string Say, string? Error);

/// <summary>
/// S2 dialogue latency and cost bench: streams one turn in 22 §4.8's output form (CHOICE / RAPPORT / SAY) and
/// records time to the first token, to the first visible content (TTFT), to the completed CHOICE line (the
/// decision gesture), to the first spoken word, and to the end; plus billed tokens and cost (OpenRouter
/// <c>usage.include</c>) and the serving provider. Generation settings follow 22 §4.8.
/// </summary>
public static class DialogueBench
{
    public static async Task<BenchTurn> RunTurnAsync(OpenAiCompatibleChatProvider chat, string model, string routing, int turn,
        string system, string user, IReadOnlyCollection<string> optionIds, IReadOnlyCollection<string> rapportIds,
        JsonObject? providerPrefs, CancellationToken ct)
    {
        var request = new ChatRequest(model, [new("system", system), new("user", user)], MaxTokens: 140, Temperature: 0.7);
        using var message = chat.Build(request, stream: true, extra: p =>
        {
            p["top_p"] = 0.9;
            p["stop"] = new JsonArray("<player_said");   // not "\n\n": Qwen3 opens with a blank line → empty reply (S2 finding)
            p["usage"] = new JsonObject { ["include"] = true };
            if (providerPrefs is not null) { p["provider"] = providerPrefs.DeepClone(); }
        });

        var sw = Stopwatch.StartNew();
        double firstToken = -1, firstContent = -1, choiceAt = -1, firstWords = -1;
        var text = new StringBuilder();
        var reasoningSeen = false;
        string provider = "?";
        int tokensIn = 0, tokensOut = 0;
        double cost = 0;
        try
        {
            using var response = await chat.Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Failed($"HTTP {(int)response.StatusCode}: {Trim(body)}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) { continue; }
                var data = line[5..].Trim();
                if (data == "[DONE]") { break; }
                var json = JsonNode.Parse(data);
                if (json?["error"] is { } err) { return Failed($"stream error: {Trim(err.ToJsonString())}"); }
                if (json?["provider"]?.GetValue<string>() is { } p) { provider = p; }
                if (json?["usage"] is JsonObject usage)
                {
                    tokensIn = (int?)usage["prompt_tokens"] ?? tokensIn;
                    tokensOut = (int?)usage["completion_tokens"] ?? tokensOut;
                    cost = (double?)usage["cost"] ?? cost;
                }

                var delta = json?["choices"]?[0]?["delta"];
                var reasoning = delta?["reasoning"]?.GetValue<string>();
                var content = delta?["content"]?.GetValue<string>();
                if ((!string.IsNullOrEmpty(reasoning) || !string.IsNullOrEmpty(content)) && firstToken < 0) { firstToken = sw.Elapsed.TotalMilliseconds; }
                if (!string.IsNullOrEmpty(reasoning)) { reasoningSeen = true; }
                if (string.IsNullOrEmpty(content)) { continue; }
                if (firstContent < 0) { firstContent = sw.Elapsed.TotalMilliseconds; }
                text.Append(content);
                var s = text.ToString();
                if (choiceAt < 0 && ChoiceLineComplete(s)) { choiceAt = sw.Elapsed.TotalMilliseconds; }
                if (firstWords < 0 && SayStarted(s)) { firstWords = sw.Elapsed.TotalMilliseconds; }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return Failed(ex is TaskCanceledException ? "timeout" : ex.GetType().Name);
        }

        var total = sw.Elapsed.TotalMilliseconds;
        var output = text.ToString();
        var (choice, rapport, say) = ParseHeader(output);
        var valid = choice is not null && optionIds.Contains(choice) && (rapport is null || rapport == "none" || rapportIds.Contains(rapport)) && say.Length > 0;
        return new BenchTurn(model, routing, turn, provider, firstToken, firstContent, choiceAt, firstWords, total,
            tokensIn, tokensOut, cost, choice, rapport, valid, reasoningSeen || output.Contains("<think>", StringComparison.Ordinal), say, null);

        BenchTurn Failed(string error)
            => new(model, routing, turn, provider, firstToken, firstContent, choiceAt, firstWords, sw.Elapsed.TotalMilliseconds,
                tokensIn, tokensOut, cost, null, null, false, reasoningSeen, "", error);
    }

    /// <summary>The CHOICE line is complete once a newline follows "CHOICE:" — the moment the decision is known.</summary>
    internal static bool ChoiceLineComplete(string s)
    {
        var at = s.IndexOf("CHOICE:", StringComparison.Ordinal);
        return at >= 0 && s.IndexOf('\n', at) > at;
    }

    /// <summary>First words: at least one non-space character after "SAY:".</summary>
    internal static bool SayStarted(string s)
    {
        var at = s.IndexOf("SAY:", StringComparison.Ordinal);
        return at >= 0 && s[(at + 4)..].Trim().Length > 0;
    }

    internal static (string? Choice, string? Rapport, string Say) ParseHeader(string output)
    {
        string? choice = null, rapport = null;
        var say = "";
        var lines = OpenAiCompatibleChatProvider.StripThink(output).Split('\n');
        var first = lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        if (!first.StartsWith("CHOICE:", StringComparison.Ordinal)) { return (null, null, ""); }   // prose before the header is malformed (22 §15)
        foreach (var raw in lines)
        {
            var l = raw.Trim();
            if (l.StartsWith("CHOICE:", StringComparison.Ordinal)) { choice = l[7..].Trim(); }
            else if (l.StartsWith("RAPPORT:", StringComparison.Ordinal)) { rapport = l[8..].Trim(); }
            else if (l.StartsWith("SAY:", StringComparison.Ordinal)) { say = l[4..].Trim(); }
            else if (say.Length > 0) { say += " " + l; }
        }

        return (choice, rapport, say.Trim());
    }

    private static string Trim(string s) => s.Length > 160 ? s[..160] : s;
}
