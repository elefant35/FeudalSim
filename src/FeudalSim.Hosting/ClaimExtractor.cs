using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Hosting;

/// <summary>A claim the player states about someone (16 §7.10): predicate id, who did it, to whom, and "I saw it".</summary>
public sealed record ExtractedClaim(string Predicate, EntityId Subject, EntityId Object, bool FirstHand);

/// <summary>
/// The M1 Claim pack (22 §4.5) without a model: the predicate's verb comes from its content phrase ("{subject} stole from
/// {object}" → "stole from"), the subject is a known name before the verb, the object a name (or "me" / "you") after it.
/// Deterministic, so template mode can report a deed (22 §13.3) and LLM mode gets the same structured claim.
/// </summary>
public sealed class ClaimExtractor
{
    private readonly (string Verb, string Id)[] _verbs;

    public ClaimExtractor(IReadOnlyList<ClaimPredicateDef> predicates)
    {
        _verbs = [.. predicates.Select(p => (Verb: Verb(p.Phrase), p.Id)).Where(v => v.Verb.Length >= 4).OrderByDescending(v => v.Verb.Length)];
    }

    public static string Verb(string phrase)
    {
        var s = phrase.IndexOf("{subject}", StringComparison.Ordinal);
        if (s < 0) { return ""; }
        var rest = phrase[(s + 9)..];
        var o = rest.IndexOf("{object}", StringComparison.Ordinal);
        return (o < 0 ? rest : rest[..o]).Trim().ToLowerInvariant();
    }

    public ExtractedClaim? Extract(string text, IReadOnlyList<(string Name, EntityId Id)> people, EntityId speaker, EntityId listener)
    {
        var t = text.ToLowerInvariant();
        foreach (var (verb, id) in _verbs)
        {
            var at = t.IndexOf(verb, StringComparison.Ordinal);
            if (at < 0) { continue; }
            var subject = Nearest(t[..at], people, speaker, listener, last: true);
            if (subject.IsNone) { continue; }
            var obj = Nearest(t[(at + verb.Length)..], people, speaker, listener, last: false);
            var firstHand = t.Contains("i saw", StringComparison.Ordinal) || t.Contains("i watched", StringComparison.Ordinal) || t.Contains("with my own eyes", StringComparison.Ordinal);
            return new ExtractedClaim(id, subject, obj, firstHand);
        }

        return null;
    }

    /// <summary>The person named closest to the verb: a first or full name, or "me"/"I" (the speaker) and "you" (the listener) after it.</summary>
    private static EntityId Nearest(string span, IReadOnlyList<(string Name, EntityId Id)> people, EntityId speaker, EntityId listener, bool last)
    {
        var best = EntityId.None;
        var bestAt = last ? -1 : int.MaxValue;
        var shared = people.GroupBy(p => p.Name.Split(' ')[0], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key.ToLowerInvariant()).ToHashSet();
        foreach (var (name, id) in people)
        {
            var first = name.Split(' ')[0].ToLowerInvariant();
            foreach (var key in shared.Contains(first) ? [name.ToLowerInvariant()] : new[] { name.ToLowerInvariant(), first }.Distinct())
            {
                var i = last ? LastWord(span, key) : FirstWord(span, key);
                if (i >= 0 && (last ? i > bestAt : i < bestAt)) { (best, bestAt) = (id, i); }
            }
        }

        if (!last)
        {
            foreach (var (word, who) in new[] { ("me", speaker), ("you", listener) })
            {
                var i = FirstWord(span, word);
                if (i >= 0 && i < bestAt) { (best, bestAt) = (who, i); }
            }
        }

        return best;
    }

    private static int FirstWord(string s, string w)
    {
        for (var i = s.IndexOf(w, StringComparison.Ordinal); i >= 0; i = s.IndexOf(w, i + 1, StringComparison.Ordinal)) { if (IsWord(s, i, w.Length)) { return i; } }
        return -1;
    }

    private static int LastWord(string s, string w)
    {
        for (var i = s.LastIndexOf(w, StringComparison.Ordinal); i >= 0; i = i == 0 ? -1 : s.LastIndexOf(w, i - 1, StringComparison.Ordinal)) { if (IsWord(s, i, w.Length)) { return i; } }
        return -1;
    }

    private static bool IsWord(string s, int i, int n) => (i == 0 || !char.IsLetterOrDigit(s[i - 1])) && (i + n >= s.Length || !char.IsLetterOrDigit(s[i + n]));
}
