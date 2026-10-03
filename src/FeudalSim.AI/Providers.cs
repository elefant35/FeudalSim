using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace FeudalSim.AI;

public sealed record ChatMessage(string Role, string Content);

public sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, int MaxTokens = 120, double Temperature = 0.7);

public sealed record ChatResult(string Text, int TokensIn, int TokensOut, int LatencyMs, double CostUsd, string ProviderTag);

public interface IChatProvider
{
    string Tag { get; }
    Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct);
    IAsyncEnumerable<string> StreamAsync(ChatRequest request, CancellationToken ct);
}

/// <summary>
/// Any OpenAI-compatible chat-completions endpoint (OpenRouter now; llama.cpp / Ollama / LM Studio later).
/// Qwen thinking mode is disabled; stray &lt;think&gt; blocks are stripped.
/// </summary>
public sealed class OpenAiCompatibleChatProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly Secret _key;

    public OpenAiCompatibleChatProvider(HttpClient http, string baseUrl, Secret key)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _key = key;
    }

    internal HttpClient Http => _http;

    public string Tag => _baseUrl.Contains("openrouter", StringComparison.OrdinalIgnoreCase) ? "openrouter" : "openai-compatible";

    public async Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var response = await _http.SendAsync(Build(request, stream: false), ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { throw new AiProviderException($"HTTP {(int)response.StatusCode}", (int)response.StatusCode); }
        var json = JsonNode.Parse(body) ?? throw new AiProviderException("empty response", 0);
        if (json["error"] is { } err) { throw new AiProviderException($"provider error {err["code"]}", (int?)err["code"] ?? 0); }
        var text = StripThink(json["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "");
        var usage = json["usage"];
        return new ChatResult(text, (int?)usage?["prompt_tokens"] ?? 0, (int?)usage?["completion_tokens"] ?? 0,
            (int)sw.ElapsedMilliseconds, (double?)usage?["cost"] ?? 0, Tag);
    }

    public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        using var response = await _http.SendAsync(Build(request, stream: true), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) { throw new AiProviderException($"HTTP {(int)response.StatusCode}", (int)response.StatusCode); }
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) { continue; }
            var data = line[5..].Trim();
            if (data == "[DONE]") { yield break; }
            var delta = JsonNode.Parse(data)?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(delta)) { yield return delta; }
        }
    }

    internal HttpRequestMessage Build(ChatRequest request, bool stream, Action<JsonObject>? extra = null)
    {
        // OpenRouter's reasoning.enabled=false is not honored by every provider (DeepInfra ignored it for
        // qwen/qwen3-14b on 2026-10-03), so Qwen3 models also get Qwen's own /no_think soft switch.
        var noThink = request.Model.Contains("qwen3", StringComparison.OrdinalIgnoreCase);
        var lastUser = request.Messages.Select((m, i) => (m, i)).LastOrDefault(x => x.m.Role == "user").i;
        var messages = new JsonArray();
        for (var i = 0; i < request.Messages.Count; i++)
        {
            var m = request.Messages[i];
            var content = noThink && i == lastUser && request.MaxTokens > 1 ? m.Content + " /no_think" : m.Content;
            messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = content });
        }
        var payload = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["stream"] = stream,
            ["reasoning"] = new JsonObject { ["enabled"] = false },
        };
        extra?.Invoke(payload);
        var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key.Reveal());
        message.Headers.Add("X-Title", "FeudalSim");
        return message;
    }

    internal static string StripThink(string text)
    {
        var start = text.IndexOf("<think>", StringComparison.Ordinal);
        var end = text.IndexOf("</think>", StringComparison.Ordinal);
        return (start >= 0 && end > start ? text.Remove(start, end - start + 8) : text).Trim();
    }
}

public sealed class AiProviderException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}
