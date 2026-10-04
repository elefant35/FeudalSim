using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FeudalSim.AI;

/// <summary>
/// <c>AI_GATEWAY_MODE</c> (20 §11) as an HTTP pipeline stage under every provider:
/// <c>live</c> passes through; <c>record</c> passes through and, when <c>LLM_LOG_TRANSCRIPTS=true</c>, stores each
/// request/response pair in <c>llm_transcripts/&lt;key&gt;.json</c>; <c>replay</c> serves stored responses by request
/// key and never touches the network — a miss is a provider failure, so the normal fallback chain applies.
/// The key is SHA-256 of method, path and body; headers (the API key) are never hashed or stored.
/// Recording buffers streamed responses whole.
/// </summary>
public sealed class TranscriptHandler(string mode, bool logTranscripts, string directory, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public string Mode { get; } = mode;

    public string Directory { get; } = directory;

    public static string Key(HttpMethod method, string path, string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{method} {path}\n{body}"))).ToLowerInvariant()[..32];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recording = Mode == "record" && logTranscripts;
        if (Mode != "replay" && !recording) { return await base.SendAsync(request, cancellationToken).ConfigureAwait(false); }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var path = request.RequestUri?.AbsolutePath ?? "";
        var file = Path.Combine(Directory, Key(request.Method, path, body) + ".json");
        if (Mode == "replay")
        {
            if (!File.Exists(file)) { throw new HttpRequestException($"replay miss: no transcript {Path.GetFileName(file)}"); }
            var stored = JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false))!;
            return new HttpResponseMessage((HttpStatusCode)stored["status"]!.GetValue<int>())
            {
                Content = new StringContent(stored["response"]!.GetValue<string>(), Encoding.UTF8, stored["content_type"]?.GetValue<string>() ?? "application/json"),
                RequestMessage = request,
            };
        }

        using var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
        JsonNode? requestJson;
        try { requestJson = JsonNode.Parse(body); }
        catch (System.Text.Json.JsonException) { requestJson = body; }

        var record = new JsonObject
        {
            ["path"] = path,
            ["request"] = requestJson,
            ["status"] = (int)response.StatusCode,
            ["content_type"] = mediaType,
            ["response"] = text,
        };
        System.IO.Directory.CreateDirectory(Directory);
        var tmp = file + ".tmp";
        await File.WriteAllTextAsync(tmp, record.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), cancellationToken).ConfigureAwait(false);
        File.Move(tmp, file, overwrite: true);
        return new HttpResponseMessage(response.StatusCode) { Content = new StringContent(text, Encoding.UTF8, mediaType), RequestMessage = request };
    }
}
