using System.Text.RegularExpressions;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.AI.Dialogue;

/// <summary>What the prompt builder may use for one NPC turn (22 §4.7 ConversationSnapshot, M1 form). Built on the sim thread.</summary>
public sealed record PromptFacts(
    string Persona, string Now, string Relationship, IReadOnlyList<string> Transcript, IReadOnlyList<string> Knows, IReadOnlyList<string> KnownNames,
    int MaxWords = 45, float Temperature = 0.7f);

/// <summary>
/// One NPC turn's decision points, bundled into one decision-first generation (22 §4.1, 21 §14.5): the primary DP (the
/// response to the player's act, or the initiative DP when the act has none), the initiative DP beside it, and the
/// conversation's rapport DP when one is open. <c>Slots</c> holds each option's template slot values ({price} …).
/// </summary>
public sealed record TurnBundle(
    ulong Conversation, int Turn, EntityId Npc, string NpcName, string PlayerName, string PlayerLine, string Act,
    DecisionPointOpened Primary, DecisionPointOpened? Initiative, DecisionPointOpened? Rapport, PromptFacts Facts,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Slots);

/// <summary>Gateway → UI only (22 §4.2): the public face of a choice — never the menu, p_i, stakes or decider.</summary>
public sealed record DecisionSurfaced(ulong Conversation, int TurnIndex, EntityId Chooser, string GestureTag, string? PerceivedEffect);

/// <summary>Gateway → UI: a verified sentence of a Tier A stream (shown as it arrives) or the whole Tier B line.</summary>
public sealed record PartialLine(ulong Conversation, int TurnIndex, EntityId Speaker, string Text, bool Final);

/// <summary>Template lines from content (canon §13.5): a variant chosen by a pure function of the DP id; slots filled.</summary>
public sealed class TemplateBank(ContentDatabase content)
{
    private readonly Dictionary<string, LineTemplateDef> _byOption = content.Lines.GroupBy(l => l.Option, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public bool Has(string option) => _byOption.ContainsKey(option);

    public string Gesture(string option) => _byOption.TryGetValue(option, out var l) ? l.Gesture : "neutral";

    public string Render(string option, ulong key, IReadOnlyDictionary<string, string>? slots)
    {
        if (!_byOption.TryGetValue(option, out var line)) { return "…"; }
        var text = line.Variants[(int)(SplitMix64.Avalanche(key ^ 0x7E4D_1A7E) % (ulong)line.Variants.Count)];
        if (slots is not null)
        {
            foreach (var (k, v) in slots) { text = text.Replace("{" + k + "}", v, StringComparison.Ordinal); }
        }

        return Regex.Replace(text, @"\{[a-z_]+\}", "that");   // an unfilled slot never shows braces
    }
}

/// <summary>22 §4.9's deterministic rule checks on speech (all tiers). Returns the cleaned text, or a failure reason.</summary>
public static partial class SpeechChecks
{
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "Aye", "Nay", "No", "Yes", "Well", "The", "A", "An", "And", "But", "So", "Then", "That", "This", "You", "Your", "My", "We", "Our", "They",
        "He", "She", "It", "Is", "Are", "Was", "If", "Oh", "Ah", "Mm", "Hm", "Ha", "Good", "Fine", "Right", "Come", "Go", "Get", "Look", "Listen", "Enough",
        "What", "Why", "Who", "Where", "When", "How", "Not", "Do", "Don't", "Can't", "Won't", "Let", "Let's", "Hearthday", "Spring", "Summer", "Autumn",
        "Winter", "God", "Lord", "Sir", "Madam", "Friend", "One", "Two", "Three", "Fair", "Done", "All", "Still", "Just", "Make", "Keep", "Take", "Give",
    };

    public static (string? Text, string? Failure) Check(string raw, int maxWords, IReadOnlySet<long> allowedNumbers, IReadOnlyList<string> knownNames)
    {
        var text = ThinkBlock().Replace(raw, " ");
        text = StageDirection().Replace(text, " ");
        text = Regex.Replace(text, @"\s{2,}", " ").Trim().Trim('"', '“', '”').Trim();
        if (text.Length == 0) { return (null, "empty"); }
        if (NonLatin().IsMatch(text)) { return (null, "script"); }
        if (Refusal().IsMatch(text)) { return (null, "refusal"); }
        if (OutOfWorld().IsMatch(text)) { return (null, "out_of_world"); }
        foreach (var n in Extractor.Numbers(text))
        {
            if (!allowedNumbers.Contains(n.Value)) { return (null, $"number:{n.Value}"); }
        }

        var unknown = 0;
        var words = Regex.Matches(text, @"(?<=[a-z,;:]\s)([A-Z][a-z']+)").Select(m => m.Value).Distinct(StringComparer.Ordinal);
        foreach (var w in words)
        {
            if (!Common.Contains(w) && !knownNames.Any(k => k.Split(' ').Contains(w, StringComparer.OrdinalIgnoreCase))) { unknown++; }
        }

        if (unknown >= 2) { return (null, "unknown_names"); }
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > maxWords * 1.3)
        {
            var cut = string.Join(' ', tokens.Take((int)(maxWords * 1.3)));
            var end = Math.Max(cut.LastIndexOf('.'), Math.Max(cut.LastIndexOf('!'), cut.LastIndexOf('?')));
            text = end > 0 ? cut[..(end + 1)] : cut + "…";
        }

        return (text, null);
    }

    /// <summary>Numbers a line may carry: those in the chosen option's slot values and facts, and any the player just said.</summary>
    public static HashSet<long> AllowedNumbers(IEnumerable<string> facts, string playerLine)
    {
        var set = new HashSet<long> { 1 };   // "one" as a word is everywhere ("the one I made")
        foreach (var f in facts.Append(playerLine)) { foreach (var n in Extractor.Numbers(f)) { set.Add(n.Value); } }
        return set;
    }

    /// <summary>The end (exclusive) of the first complete sentence starting at <paramref name="from"/>, or 0 if none yet.</summary>
    public static int NextSentenceEnd(string s, int from)
    {
        for (var i = from; i < s.Length; i++)
        {
            if (s[i] is '.' or '!' or '?' && i + 1 < s.Length && s[i + 1] == ' ') { return i + 1; }
        }

        return 0;
    }

    /// <summary>Complete sentences in a growing buffer (for Tier A streaming): returns how many characters end a sentence.</summary>
    public static int SentenceEnd(string s)
    {
        var last = -1;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is '.' or '!' or '?' && (i + 1 == s.Length || s[i + 1] == ' ')) { last = i; }
        }

        return last + 1;
    }

    [GeneratedRegex(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"\*[^*]{0,120}\*|\([^)]{0,120}\)|\[[^\]]{0,120}\]")]
    private static partial Regex StageDirection();

    [GeneratedRegex(@"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}\p{IsCyrillic}\p{IsArabic}\p{IsHebrew}\p{IsDevanagari}\p{IsThai}]")]
    private static partial Regex NonLatin();

    [GeneratedRegex(@"\b(i can'?t help with|as an ai|i'?m (just )?an ai|language model|i cannot comply|i'?m not able to (do|help)|policy|guidelines)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Refusal();

    [GeneratedRegex(@"\b(ai|a\.i\.|chatgpt|prompt|computer|internet|online|email|phone|video game|the game|player|npc|server|okay|ok|robot|software|app|website)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OutOfWorld();
}
