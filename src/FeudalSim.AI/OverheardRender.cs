using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FeudalSim.AI;

/// <summary>
/// Overheard talk (22 §9.2): a speak-only prompt over the sim's facts, and the post-generation check (rules + speaker
/// check). The sim already decided the exchange; a reply that fails the check is refused and the template subtitle plays.
/// </summary>
public static partial class OverheardRender
{
    public const int MinLines = 2, MaxLines = 6, MaxWordsPerLine = 30;

    // Subtitles and logs stay readable (’ not \u2019); the text is never embedded in HTML.
    private static readonly JsonSerializerOptions Readable = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static IReadOnlyList<ChatMessage> Messages(string factsJson) =>
    [
        new("system",
            "You write overheard dialogue for a medieval settlement game. The game has already decided what happens; you only voice it.\n" +
            "Rules:\n" +
            $"- Write {MinLines}-{MaxLines} short lines as a JSON array: [{{\"speaker\": \"<name>\", \"line\": \"<words>\"}}]. Output the JSON only.\n" +
            "- Only the two people in \"a\" and \"b\" speak. \"a\" starts.\n" +
            "- Voice exactly the facts given. Do not add events, accusations, people or claims beyond them. Use only names from \"names\".\n" +
            "- Do not mention any person, animal, building, trade or happening that is not in the facts. Small talk stays on what the setting holds: the work, food, the fire, the weather, the sea crossing, the place they are.\n" +
            "- If \"claim\" is given, the speaker passes it on as hearsay and the listener reacts as \"listener_choice\" says.\n" +
            "- \"success\": false means it went badly (a joke falls flat, a request is refused, an apology is not accepted).\n" +
            $"- Plain, period-appropriate English; no modern idioms; at most 25 words per line; fit each person's temperament and feeling."),
        new("user", factsJson),
    ];

    /// <summary>
    /// Validates a reply against the facts: JSON array of 2–6 {speaker, line}; speakers are a and b; lines non-empty and
    /// ≤ 30 words; no "Settler N"-style name outside the allowed names; the claim's subject is named when a claim is voiced.
    /// Returns the normalized JSON (compact) or null with a reason.
    /// </summary>
    public static string? Validate(string factsJson, string reply, out string reason)
    {
        var facts = JsonNode.Parse(factsJson)!.AsObject();
        var a = (string)facts["a"]!["name"]!;
        var b = (string)facts["b"]!["name"]!;
        var allowed = facts["names"]!.AsArray().Select(n => (string)n!).ToHashSet(StringComparer.Ordinal);
        var claimAbout = (string?)facts["claim_about"];

        var start = reply.IndexOf('[');
        var end = reply.LastIndexOf(']');
        if (start < 0 || end <= start) { reason = "no-json-array"; return null; }
        JsonArray lines;
        try { lines = JsonNode.Parse(reply[start..(end + 1)])!.AsArray(); }
        catch (JsonException) { reason = "bad-json"; return null; }
        catch (InvalidOperationException) { reason = "bad-json"; return null; }

        if (lines.Count is < MinLines or > MaxLines) { reason = $"line-count:{lines.Count}"; return null; }
        var output = new JsonArray();
        var mentioned = false;
        foreach (var node in lines)
        {
            if (node is not JsonObject o || o["speaker"]?.GetValueKind() != JsonValueKind.String || o["line"]?.GetValueKind() != JsonValueKind.String) { reason = "bad-entry"; return null; }
            var speaker = ((string)o["speaker"]!).Trim();
            var line = ((string)o["line"]!).Trim();
            if (speaker != a && speaker != b) { reason = "unknown-speaker"; return null; }
            if (line.Length == 0) { reason = "empty-line"; return null; }
            if (line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxWordsPerLine) { reason = "too-long"; return null; }
            foreach (Match m in SettlerName().Matches(line)) { if (!allowed.Contains(m.Value)) { reason = "invented-name"; return null; } }
            mentioned |= claimAbout is not null && line.Contains(claimAbout, StringComparison.Ordinal);
            output.Add(new JsonObject { ["speaker"] = speaker, ["line"] = line });
        }

        if (claimAbout is not null && !mentioned && claimAbout != a && claimAbout != b) { reason = "claim-not-voiced"; return null; }
        reason = "ok";
        return output.ToJsonString(Readable);
    }

    /// <summary>Graybox camp names ("Settler 7"); real names (M1-18) extend this check to the settlement's name list.</summary>
    [GeneratedRegex(@"\bSettler \d+\b")]
    private static partial Regex SettlerName();
}
