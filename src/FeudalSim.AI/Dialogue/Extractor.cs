using System.Globalization;
using System.Text.RegularExpressions;

namespace FeudalSim.AI.Dialogue;

public sealed record NumberMention(long Value, int Start, int Length, bool Approximate);

public sealed record MoneyMention(long Farthings, int Start, int Length);

public sealed record NameMention(string Name, int Index, bool Exact);

public sealed record ItemMention(string ItemId, int Qty, int Start);

/// <summary>Everything deterministic pulled from one line (22 §4.5). Numbers never come from a model.</summary>
public sealed record ExtractionRecord(IReadOnlyList<NumberMention> Numbers, IReadOnlyList<MoneyMention> Money, IReadOnlyList<NameMention> Names, IReadOnlyList<ItemMention> Items);

/// <summary>
/// 22 §4.5 step 2: the number parser (digits and number words: "seventy", "a dozen", "a score", "half", "a couple", "a
/// brace"), the currency parser (farthing/f, penny/pence/d, shilling/s, crown; "two and six"; bare numbers inside an
/// active haggle take its unit), item mentions from content names and aliases, and a name matcher (exact, or Levenshtein
/// ≤ 2 for names of 5+ letters) against the people the speaker could plausibly know.
/// </summary>
public static partial class Extractor
{
    public const long PennyF = 4, ShillingF = 48, CrownF = 960;

    private static readonly Dictionary<string, long> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["a"] = 1, ["an"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7,
        ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15,
        ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,
    };

    private static readonly Dictionary<string, long> Tens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,
    };

    private static readonly Dictionary<string, (long Value, bool Approx)> Collective = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dozen"] = (12, false), ["score"] = (20, false), ["couple"] = (2, true), ["brace"] = (2, false), ["pair"] = (2, false), ["few"] = (3, true), ["half"] = (0, false),
    };

    /// <summary>Parses numbers and money; <paramref name="defaultUnitF"/> prices bare numbers inside a haggle (e.g. 1 = farthings).</summary>
    public static ExtractionRecord Extract(string text, IReadOnlyList<string> knownNames, IReadOnlyList<(string Id, string[] Names)> items, long? defaultUnitF = null)
    {
        var numbers = Numbers(text);
        var money = Money(text, numbers, defaultUnitF);
        return new ExtractionRecord(numbers, money, MatchNames(text, knownNames), MatchItems(text, items, numbers));
    }

    public static List<NumberMention> Numbers(string text)
    {
        var found = new List<NumberMention>();
        foreach (Match m in Digits().Matches(text))
        {
            if (long.TryParse(m.Value.Replace(",", "", StringComparison.Ordinal), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) { found.Add(new(v, m.Index, m.Length, false)); }
        }

        foreach (Match m in WordNumber().Matches(text))
        {
            var words = m.Value.ToLowerInvariant().Replace("-", " ", StringComparison.Ordinal).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            long total = 0, current = 0;
            var any = false;
            var approx = false;
            foreach (var w in words)
            {
                if (w is "and") { continue; }
                if (Units.TryGetValue(w, out var u) && w is not ("a" or "an")) { current += u; any = true; }
                else if (Tens.TryGetValue(w, out var t)) { current += t; any = true; }
                else if (w == "hundred") { current = Math.Max(1, current) * 100; any = true; }
                else if (Collective.TryGetValue(w, out var c)) { current = c.Value == 0 ? current : Math.Max(1, current) * c.Value; approx |= c.Approx; any |= c.Value > 0; }
            }

            total += current;
            if (any && total > 0) { found.Add(new(total, m.Index, m.Length, approx)); }
        }

        return [.. found.OrderBy(n => n.Start)];
    }

    /// <summary>Money in farthings: "5d", "two pence", "three shillings", "a crown", "two and six" (2s 6d), "seventy farthings".</summary>
    public static List<MoneyMention> Money(string text, IReadOnlyList<NumberMention> numbers, long? defaultUnitF)
    {
        var found = new List<MoneyMention>();
        foreach (Match m in TwoAndSix().Matches(text))
        {
            var s = WordOrDigit(m.Groups[1].Value);
            var d = WordOrDigit(m.Groups[2].Value);
            if (s > 0) { found.Add(new((s * ShillingF) + (d * PennyF), m.Index, m.Length)); }
        }

        foreach (var n in numbers)
        {
            if (found.Any(f => n.Start >= f.Start && n.Start < f.Start + f.Length)) { continue; }
            var after = text[(n.Start + n.Length)..].TrimStart();
            var unit = UnitAt(after);
            if (unit is { } u) { found.Add(new(n.Value * u, n.Start, n.Length)); }
            else if (defaultUnitF is { } du && !after.StartsWith('%')) { found.Add(new(n.Value * du, n.Start, n.Length)); }
        }

        foreach (Match m in ACrown().Matches(text)) { found.Add(new(CrownF, m.Index, m.Length)); }
        return [.. found.OrderBy(f => f.Start)];
    }

    private static long? UnitAt(string after)
    {
        var w = UnitWord().Match(after);
        if (!w.Success) { return null; }
        return w.Groups[1].Value.ToLowerInvariant() switch
        {
            "f" or "farthing" or "farthings" => 1,
            "d" or "penny" or "pence" or "pennies" => PennyF,
            "s" or "shilling" or "shillings" or "bob" => ShillingF,
            "crown" or "crowns" => CrownF,
            _ => null,
        };
    }

    private static long WordOrDigit(string s)
        => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : Units.TryGetValue(s, out var u) ? u : Tens.GetValueOrDefault(s);

    /// <summary>Names the speaker could know: exact (case-insensitive, any word of the name) or fuzzy (Levenshtein ≤ 2, names ≥ 5 letters).</summary>
    public static List<NameMention> MatchNames(string text, IReadOnlyList<string> knownNames)
    {
        var words = Word().Matches(text).Select(m => m.Value).ToArray();
        var found = new List<NameMention>();
        for (var i = 0; i < knownNames.Count; i++)
        {
            var parts = knownNames[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var exact = parts.Any(p => p.Length >= 3 && words.Any(w => string.Equals(w, p, StringComparison.OrdinalIgnoreCase)))
                        || text.Contains(knownNames[i], StringComparison.OrdinalIgnoreCase);
            var fuzzy = !exact && parts.Any(p => p.Length >= 5 && words.Any(w => Math.Abs(w.Length - p.Length) <= 2 && Levenshtein(w.ToLowerInvariant(), p.ToLowerInvariant()) <= 2));
            if (exact || fuzzy) { found.Add(new(knownNames[i], i, exact)); }
        }

        return found;
    }

    public static List<ItemMention> MatchItems(string text, IReadOnlyList<(string Id, string[] Names)> items, IReadOnlyList<NumberMention> numbers)
    {
        var found = new List<ItemMention>();
        foreach (var (id, names) in items)
        {
            foreach (var name in names.OrderByDescending(n => n.Length))
            {
                var at = text.IndexOf(name, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { continue; }
                var qty = numbers.LastOrDefault(n => n.Start + n.Length <= at && at - (n.Start + n.Length) <= 3)?.Value ?? 1;
                found.Add(new(id, (int)Math.Clamp(qty, 1, 999), at));
                break;
            }
        }

        return [.. found.OrderBy(f => f.Start)];
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) { prev[j] = j; }
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++) { cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)); }
            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }

    [GeneratedRegex(@"(?<![\w,])\d{1,3}(,\d{3})+(?!\d)|(?<![\w,])\d+(?![\d,])", RegexOptions.CultureInvariant)]
    private static partial Regex Digits();

    [GeneratedRegex(@"\b((?:a|an|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|and|dozen|score|couple|brace|pair|few|half)(?:[\s-]+(?:a|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|and|dozen|score|couple|brace|pair|few|half|of))*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordNumber();

    [GeneratedRegex(@"\b(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\s+and\s+(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven)\b(?!\s*(?:f|d|s|farthings?|penny|pence|shillings?|crowns?)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TwoAndSix();

    [GeneratedRegex(@"^(f|d|s|farthings?|penny|pence|pennies|shillings?|bob|crowns?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitWord();

    [GeneratedRegex(@"\ba\s+crown\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ACrown();

    [GeneratedRegex(@"[\p{L}']+", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
