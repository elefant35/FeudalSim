using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FeudalSim.AI.Dialogue;

/// <summary>What the sanitizer did to one player line (22 §4.4).</summary>
public sealed record Sanitized(string Text, bool Truncated, bool InjectionHeuristic, IReadOnlyList<string> Flags);

/// <summary>
/// 22 §4.4 step 1. Player text is untrusted data (canon §13): NFKC-normalize (<see cref="Fold"/>); strip control and zero-width characters;
/// collapse whitespace and any character repeated more than 4 times; cap at 280 characters (setting up to 500) with an
/// ellipsis; remove model control tokens and our delimiters; escape the decision-header keywords (so the NPC can still
/// react to strangeness); and flag the heuristic injection lexicon (with it, the turn's DPs go to the policy).
/// </summary>
public static partial class Sanitizer
{
    public const int DefaultMaxChars = 280;
    public const int MaxMaxChars = 500;

    private static readonly string[] ControlTokens =
        ["<|im_start|>", "<|im_end|>", "<|endoftext|>", "<think>", "</think>", "<player_said", "</player_said>", "<said>", "</said>"];

    public static Sanitized Clean(string raw, int maxChars = DefaultMaxChars)
    {
        maxChars = Math.Clamp(maxChars, 1, MaxMaxChars);
        var flags = new List<string>();
        var s = Fold(raw ?? "");

        // Control, format (zero-width, bidi) and private-use characters go; whitespace becomes plain spaces.
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            var cat = char.GetUnicodeCategory(ch);
            if (char.IsWhiteSpace(ch)) { sb.Append(' '); continue; }
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate or UnicodeCategory.OtherNotAssigned)
            {
                if (flags.Count == 0 || flags[^1] != "stripped") { flags.Add("stripped"); }
                continue;
            }

            sb.Append(ch);
        }

        s = sb.ToString();
        foreach (var token in ControlTokens)
        {
            if (s.Contains(token, StringComparison.OrdinalIgnoreCase)) { s = s.Replace(token, " ", StringComparison.OrdinalIgnoreCase); flags.Add("control_token"); }
        }

        s = HeaderKeyword().Replace(s, m => { flags.Add("header_keyword"); return m.Groups[1].Value + "∶"; });   // "CHOICE:" → "CHOICE∶" (ratio sign)
        s = Repeats().Replace(s, m => m.Value[..4]);
        s = Spaces().Replace(s, " ").Trim();

        var truncated = false;
        if (s.Length > maxChars)
        {
            var cut = s[..(maxChars - 1)];
            var space = cut.LastIndexOf(' ');
            s = (space > maxChars / 2 ? cut[..space] : cut).TrimEnd() + "…";
            truncated = true;
            flags.Add("truncated");
        }

        var injection = Injection().IsMatch(s);
        if (injection) { flags.Add("inj_heuristic"); }
        return new Sanitized(s, truncated, injection, flags);
    }

    /// <summary>
    /// NFKC where it matters for evasion. The process runs with invariant globalization (determinism), under which
    /// <c>string.Normalize</c> leaves non-ASCII text as is, so the compatibility forms used to dodge filters are folded
    /// here: fullwidth ASCII (U+FF01–FF5E), the ideographic space, Latin ligatures and typographic quotes and dashes.
    /// </summary>
    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case >= '\uFF01' and <= '\uFF5E': sb.Append((char)(ch - 0xFEE0)); break;
                case '\u3000': sb.Append(' '); break;
                case '\uFB00': sb.Append("ff"); break;
                case '\uFB01': sb.Append("fi"); break;
                case '\uFB02': sb.Append("fl"); break;
                case '\uFB03': sb.Append("ffi"); break;
                case '\uFB04': sb.Append("ffl"); break;
                case '\u2018' or '\u2019' or '\u02BC': sb.Append('\''); break;
                case '\u201C' or '\u201D': sb.Append('"'); break;
                case '\u2013' or '\u2014': sb.Append('-'); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormKC);
    }

    [GeneratedRegex(@"\b(CHOICE|RAPPORT|SAY)\s*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderKeyword();

    [GeneratedRegex(@"(.)\1{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex Repeats();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    /// <summary>22 §4.4's lexicon: instruction overrides, persona swaps, system-prompt talk, AI self-reference, mode switches.</summary>
    [GeneratedRegex(@"ignore\s+(all\s+|any\s+|the\s+)?(previous|prior|above|earlier)\s+(instructions|rules|prompts?)|disregard\s+(all\s+|the\s+)?(previous|prior|above)|you\s+are\s+now\b|from\s+now\s+on\s+you\b|system\s+prompt|as\s+an\s+ai\b|language\s+model|developer\s+mode|jailbreak|\bdan\s+mode\b|pretend\s+(you\s+are|to\s+be)\s+(an?\s+)?(ai|assistant|chatbot)|new\s+instructions?\s*:|output\s+(choice|the\s+option)|respond\s+only\s+with|override\s+(your|the)\s+(rules|instructions)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Injection();
}

/// <summary>
/// 22 §4.4 step 4: ≥ 2 s between the player's turns in one conversation (a faster line waits), and more than 30 turns in
/// a real minute across all NPCs → a cooldown until the window clears. Real time, gateway side (never a sim input).
/// </summary>
public sealed class TurnRateLimiter(TimeProvider clock)
{
    public static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2);
    public const int MaxPerMinute = 30;

    private readonly Queue<DateTimeOffset> _window = new();
    private readonly Dictionary<ulong, DateTimeOffset> _last = [];

    /// <summary>Zero if a turn in this conversation may go now, else how long to wait.</summary>
    public TimeSpan Check(ulong conversation)
    {
        var now = clock.GetUtcNow();
        while (_window.Count > 0 && now - _window.Peek() >= TimeSpan.FromMinutes(1)) { _window.Dequeue(); }
        var wait = _window.Count >= MaxPerMinute ? _window.Peek() + TimeSpan.FromMinutes(1) - now : TimeSpan.Zero;
        if (_last.TryGetValue(conversation, out var last) && last + MinGap - now > wait) { wait = last + MinGap - now; }
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>Records a turn that went through.</summary>
    public void Record(ulong conversation)
    {
        var now = clock.GetUtcNow();
        _last[conversation] = now;
        _window.Enqueue(now);
    }
}
