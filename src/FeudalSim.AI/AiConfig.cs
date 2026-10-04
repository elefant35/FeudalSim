using System.Globalization;

namespace FeudalSim.AI;

/// <summary>A secret value that can be used but never printed (20 §18).</summary>
public sealed class Secret
{
    private readonly string? _value;

    public Secret(string? value) => _value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public bool IsSet => _value is not null;

    /// <summary>Use only to build an Authorization header.</summary>
    public string Reveal() => _value ?? throw new InvalidOperationException("Secret is not set.");

    public override string ToString() => IsSet ? "***" : "(missing)";
}

/// <summary>Configuration from the process environment and the repo's <c>.env</c> (names as in <c>.env.example</c>).</summary>
public sealed class AiConfig
{
    public string LlmMode { get; init; } = "auto";
    public Secret ChatKey { get; init; } = new(null);
    public string ChatBaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public string DialogueModel { get; init; } = "qwen/qwen3-14b";

    /// <summary><c>LLM_PROVIDER_SORT</c>: OpenRouter routing for chat (latency · throughput · price · default). S2 chose latency.</summary>
    public string ChatProviderSort { get; init; } = "latency";
    public int TtftTimeoutMs { get; init; } = 3_000;
    public int MaxConcurrency { get; init; } = 4;
    public string DeciderProvider { get; init; } = "openrouter-llm";
    public string DeciderModel { get; init; } = "qwen/qwen3.5-9b";
    public string DeciderBaseUrl { get; init; } = "https://openrouter.ai/api/v1";
    public Secret DeciderKey { get; init; } = new(null);
    public int DeciderTimeoutMs { get; init; } = 1_200;

    /// <summary><c>DECIDER_PROVIDER_SORT</c>: OpenRouter routing for the fast decider. S3 chose latency (p50 391 ms vs 620 ms).</summary>
    public string DeciderProviderSort { get; init; } = "latency";
    public double MaxSpendUsdPerSession { get; init; } = 1.00;

    /// <summary><c>AI_GATEWAY_MODE</c>: live · record · replay (20 §11). Orthogonal to <see cref="LlmMode"/>.</summary>
    public string GatewayMode { get; init; } = "live";

    /// <summary><c>LLM_LOG_TRANSCRIPTS</c>: record mode only writes transcripts when this is true.</summary>
    public bool LogTranscripts { get; init; }

    /// <summary>Where transcripts live (<c>LLM_TRANSCRIPT_DIR</c>, default <c>llm_transcripts/</c>; gitignored).</summary>
    public string TranscriptDir { get; init; } = "llm_transcripts";

    /// <summary>No provider calls at all. Replay needs no key: it never reaches the network.</summary>
    public bool TemplateMode => LlmMode == "template" || (!ChatKey.IsSet && GatewayMode != "replay");

    public static AiConfig Load(string? envFile = null)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        if (envFile is not null && File.Exists(envFile)) { foreach (var (k, v) in DotEnv.Parse(File.ReadAllLines(envFile))) { env[k] = v; } }
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            env[(string)e.Key] = (string?)e.Value ?? "";   // process environment wins over .env
        }

        string Get(string key, string fallback) => env.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? StripComment(v) : fallback;
        int GetInt(string key, int fallback) => int.TryParse(Get(key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback;
        double GetDouble(string key, double fallback) => double.TryParse(Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

        var openRouter = Get("OPENROUTER_KEY", "");
        var chatKey = Get("LLM_API_KEY", openRouter);
        var baseUrl = Get("LLM_BASE_URL", "https://openrouter.ai/api/v1");
        return new AiConfig
        {
            LlmMode = Get("LLM_MODE", "auto"),
            ChatKey = new Secret(chatKey),
            ChatBaseUrl = baseUrl,
            DialogueModel = Get("LLM_DIALOGUE_MODEL", "qwen/qwen3-14b"),
            ChatProviderSort = Get("LLM_PROVIDER_SORT", "latency"),
            TtftTimeoutMs = GetInt("LLM_TIMEOUT_TTFT_MS", 3_000),
            MaxConcurrency = GetInt("LLM_MAX_CONCURRENCY", 4),
            DeciderProvider = Get("DECIDER_PROVIDER", "openrouter-llm"),
            DeciderModel = Get("DECIDER_MODEL", "qwen/qwen3.5-9b"),
            DeciderBaseUrl = Get("DECIDER_BASE_URL", baseUrl),
            DeciderKey = new Secret(Get("DECIDER_PROVIDER", "openrouter-llm") == "typesafe" ? Get("TYPESAFE_API_KEY", "") : openRouter),
            DeciderTimeoutMs = GetInt("DECIDER_TIMEOUT_MS", 1_200),
            DeciderProviderSort = Get("DECIDER_PROVIDER_SORT", "latency"),
            MaxSpendUsdPerSession = GetDouble("LLM_MAX_SPEND_USD_PER_SESSION", 1.00),
            GatewayMode = Get("AI_GATEWAY_MODE", "live").ToLowerInvariant() is var m && m is "live" or "record" or "replay" ? m : "live",
            LogTranscripts = Get("LLM_LOG_TRANSCRIPTS", "false").Equals("true", StringComparison.OrdinalIgnoreCase),
            TranscriptDir = Get("LLM_TRANSCRIPT_DIR", "llm_transcripts"),
        };
    }

    /// <summary>Finds <c>.env</c> next to <c>FeudalSim.sln</c>, searching upward from <paramref name="start"/>.</summary>
    public static string? FindEnvFile(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln")))
            {
                var path = Path.Combine(dir.FullName, ".env");
                return File.Exists(path) ? path : null;
            }
        }

        return null;
    }

    private static string StripComment(string v)
    {
        var i = v.IndexOf(" #", StringComparison.Ordinal);
        return (i >= 0 ? v[..i] : v).Trim();
    }
}

/// <summary>Minimal dotenv parser: <c>KEY=value</c>, optional <c>export</c>, quotes, <c>#</c> comments.</summary>
public static class DotEnv
{
    public static IEnumerable<(string Key, string Value)> Parse(IEnumerable<string> lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) { continue; }
            if (line.StartsWith("export ", StringComparison.Ordinal)) { line = line[7..].TrimStart(); }
            var eq = line.IndexOf('=');
            if (eq <= 0) { continue; }
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) { value = value[1..^1]; }
            yield return (key, value);
        }
    }
}
