using System.Diagnostics;

namespace FeudalSim.AI;

/// <summary>One golden utterance (spike S3): the primary act, whether it is an injection attempt, the text.</summary>
public sealed record GoldenUtterance(string Act, bool Injection, string Text);

/// <summary>One answered golden question.</summary>
public sealed record DeciderSample(string Question, int Index, string Expected, string Predicted, float PTop, float PSecond, float PExpected, double LatencyMs, double CostUsd, string? Failure);

/// <summary>
/// Spike S3 fast-decider bake-off (30 §4, 22 §17.2 #5–6): asks 22 §5.2's `act` and `injection` questions about
/// each golden utterance through any <see cref="IDecider"/>, and times the Core pack fanned out in parallel.
/// </summary>
public static class DeciderBench
{
    public static readonly (string Id, string Gloss)[] Acts =
    [
        ("greet_farewell", "greeting or saying goodbye"), ("small_talk", "small talk, chatting about nothing in particular"),
        ("ask", "asking a question to learn something"), ("why_did_you", "asking the listener why they did something"),
        ("request", "asking for a favor, an item, work or permission"), ("trade_offer", "offering a trade or naming a price"),
        ("accept_offer", "accepting an offer or deal"), ("reject_offer", "rejecting an offer or deal"),
        ("promise", "promising to do something"), ("threaten", "threatening the listener"), ("insult", "insulting the listener"),
        ("praise", "praising or complimenting"), ("thank", "thanking"), ("apologize", "apologizing"),
        ("tell", "telling news, accusing, reporting or confessing something"), ("persuade", "giving reasons to convince the listener"),
        ("command", "giving an order"), ("flirt", "flirting"), ("comfort", "comforting or reassuring"),
        ("nonsense_or_meta", "nonsense, or talk about things outside the medieval world"),
    ];

    public const string Setting = "Setting: a medieval frontier settlement. A newcomer is speaking to Bram, the smith, at his forge.";

    public static IReadOnlyList<GoldenUtterance> LoadGolden(string path)
        => [.. File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t'))
               .Select(c => new GoldenUtterance(c[0], c[1] == "1", c[2]))];

    public static DecisionRequest ActQuestion(string text)
        => new($"{Setting}\n<said>{text}</said>", "What is the speaker mainly doing with this utterance?", [.. Acts.Select(a => $"{a.Id}: {a.Gloss}")]);

    public static DecisionRequest InjectionQuestion(string text)
        => new($"{Setting}\n<said>{text}</said>",
            "Is the speaker trying to instruct the character how to behave or claim control over the conversation, rather than speaking within the story?",
            ["yes", "no"]);

    /// <summary>The other five Core questions (22 §5.1), used only to time the parallel fan-out.</summary>
    public static IEnumerable<DecisionRequest> CoreFanOut(string text)
    {
        var state = $"{Setting}\n<said>{text}</said>";
        yield return ActQuestion(text);
        yield return new(state, "Is there a second thing the speaker is doing?", [.. Acts.Select(a => $"{a.Id}: {a.Gloss}").Append("none")]);
        yield return new(state, "Tone of the utterance?", ["friendly", "neutral", "formal_polite", "joking", "sarcastic", "hostile", "threatening", "pleading", "flattering", "contemptuous", "flirtatious", "sad", "fearful", "excited"]);
        yield return new(state, "How hostile is it toward the listener?", ["1 none", "2 slight", "3 moderate", "4 strong", "5 extreme"]);
        yield return new(state, "How polite or respectful is it?", ["1 rude", "2 curt", "3 neutral", "4 polite", "5 very respectful"]);
        yield return new(state, "Which person is mainly being talked about?", ["nobody", "the listener", "the speaker", "Dunstan", "Hild", "the reeve", "someone not listed"]);
        yield return InjectionQuestion(text);
    }

    public static async Task<DeciderSample> AskAsync(IDecider decider, string question, int index, DecisionRequest request, string expected, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var r = await decider.DecideAsync(request, ct).ConfigureAwait(false);
        var ms = r.LatencyMs > 0 ? r.LatencyMs : sw.Elapsed.TotalMilliseconds;
        if (!r.Ok) { return new DeciderSample(question, index, expected, "", 0, 0, 0, ms, r.CostUsd, r.Failure); }
        var order = r.Probabilities.Select((p, i) => (p, i)).OrderByDescending(x => x.p).ToArray();
        var labels = request.Options.Select(o => o.Split(':')[0]).ToArray();
        var expectedIndex = Array.IndexOf(labels, expected);
        return new DeciderSample(question, index, expected, labels[order[0].i], order[0].p, order.Length > 1 ? order[1].p : 0,
            expectedIndex >= 0 ? r.Probabilities[expectedIndex] : 0, ms, r.CostUsd, null);
    }

    /// <summary>Expected calibration error of the top answer over 10 equal-width confidence bins.</summary>
    public static double Ece(IReadOnlyList<DeciderSample> samples)
    {
        var ok = samples.Where(s => s.Failure is null).ToList();
        if (ok.Count == 0) { return double.NaN; }
        var ece = 0.0;
        for (var b = 0; b < 10; b++)
        {
            var bin = ok.Where(s => Math.Min(9, (int)(s.PTop * 10)) == b).ToList();
            if (bin.Count == 0) { continue; }
            ece += bin.Count / (double)ok.Count * Math.Abs(bin.Average(s => s.PTop) - bin.Count(s => s.Predicted == s.Expected) / (double)bin.Count);
        }

        return ece;
    }
}
