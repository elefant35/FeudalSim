using System.Runtime.CompilerServices;
using FeudalSim.AI;
using FeudalSim.AI.Dialogue;
using FeudalSim.Content;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.AI.Tests;

/// <summary>M1-12: the decision-first reply route (22 §4.1–4.12): header → DecisionMade, tiers, verification, regeneration, templates.</summary>
public sealed class ReplyRouterTests
{
    private static readonly TemplateBank Templates = new(ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!);

    /// <summary>Streams scripted pieces (with optional delays) and answers CompleteAsync from a queue; can throw.</summary>
    private sealed class FakeChat(IEnumerable<string> pieces, Queue<string>? completions = null, TimeSpan? delay = null, bool fail = false) : IChatProvider
    {
        public string Tag => "fake";
        public List<ChatRequest> Requests { get; } = [];

        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new ChatResult(completions?.Count > 0 ? completions.Dequeue() : "SAY: Well.", 100, 10, 300, 0.0001, Tag));
        }

        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request);
            foreach (var p in pieces)
            {
                if (delay is { } d) { await Task.Delay(d, ct); }
                if (fail) { throw new HttpRequestException("network down"); }
                yield return p;
            }
        }
    }

    private sealed class FakeVerifier(params float[] yesByCall) : IDecider
    {
        private int _n;
        public string ProviderId => "verify";

        public ValueTask<DecisionResult> DecideAsync(DecisionRequest request, CancellationToken ct)
        {
            var yes = yesByCall[Math.Min(Interlocked.Increment(ref _n) - 1, yesByCall.Length - 1)];
            return ValueTask.FromResult(new DecisionResult([yes, 1 - yes], yes > 0.5f ? 0 : 1, ProviderId, 50, 10, 0));
        }
    }

    private static DecisionPointOpened Dp(ulong id, params (string Id, float P, Stakes S)[] options)
        => new(id, "owner", new DpContext("kind", new EntityId(1), new EntityId(2), 0), 1000 + id,
            [.. options.Select(o => new MenuOption(o.Id, "f", [], true, o.P, 0f, o.S, false, $"do {o.Id}"))], [.. options.Select(o => o.Id)], 10, 50, DeciderKind.Llm);

    private static readonly DecisionPointOpened Initiative = Dp(7, ("end_conversation", 0.05f, Stakes.Low), ("invite", 0.2f, Stakes.Low), ("none", 0.75f, Stakes.Low));

    private static TurnBundle Bundle(DecisionPointOpened primary, DecisionPointOpened? initiative = null, string act = "small_talk", string line = "Nice weather.",
        Dictionary<string, IReadOnlyDictionary<string, string>>? slots = null)
        => new(1, 3, new EntityId(1), "Bram", "Tam", line, act, primary, initiative ?? primary, null,
            new PromptFacts("Name: Bram, a smith.", "Spring, evening, at the fire.", "TOWARD TAM: likes him a little.", [], [], ["Bram", "Tam", "Hild"]),
            slots ?? []);

    private static (DialogueReplyRouter R, List<DecisionMade> D, List<DialogueLineRendered> L, List<PartialLine> P, List<DecisionSurfaced> S) Router(IChatProvider? chat, IDecider? verifier = null)
    {
        var r = new DialogueReplyRouter(chat, verifier, new AiConfig(), Templates);
        var d = new List<DecisionMade>();
        var l = new List<DialogueLineRendered>();
        var p = new List<PartialLine>();
        var s = new List<DecisionSurfaced>();
        r.Decided += x => { lock (d) { d.Add(x); } };
        r.Line += l.Add;
        r.Partial += p.Add;
        r.Surfaced += s.Add;
        return (r, d, l, p, s);
    }

    [Fact]
    public async Task ValidHeader_DecidesFirst_ThenTierAStreamsSentenceBySentence()
    {
        var chat = new FakeChat(["CHOICE: inv", "ite\nRAPPORT: none\nSAY: Aye, fine", " weather. Come to the fire", " tonight, Tam."]);
        var (r, d, l, p, s) = Router(chat);
        await r.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        d.Single().ShouldBe(new DecisionMade(7, 1007, "invite", DeciderKind.Llm, d.Single().ProviderTag, d.Single().LatencyMs, null));
        s.Single().GestureTag.ShouldBe("warm_smile");
        p.Count(x => !x.Final).ShouldBeGreaterThanOrEqualTo(1);   // the first sentence was shown before the end
        p.First().Text.ShouldBe("Aye, fine weather.");
        l.Single().Text.ShouldBe("Aye, fine weather. Come to the fire tonight, Tam.");
        l.Single().Source.ShouldBe("llm");
        chat.Requests.Single().Messages[^1].Content.ShouldContain("- invite: do invite (quite possible)");   // inclination words, no numbers
    }

    [Fact]
    public async Task AuditLines_JudgesEveryModelLineAfterItIsShown()
    {
        var chat = new FakeChat(["CHOICE: invite\nRAPPORT: none\nSAY: Aye, fine weather. Come to the fire tonight, Tam."]);
        var r = new DialogueReplyRouter(chat, new FakeVerifier(0.9f, 0f, 0f, 0f), new AiConfig(), Templates) { AuditLines = true };
        var audits = new System.Collections.Concurrent.ConcurrentBag<LineAudit>();
        r.Audited += audits.Add;
        await r.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        for (var i = 0; i < 100 && audits.IsEmpty; i++) { await Task.Delay(10, TestContext.Current.CancellationToken); }
        var audit = audits.Single();
        (audit.Choice, audit.Source, audit.Failure).ShouldBe(("invite", "llm", "v_contradicts"));
    }

    [Fact]
    public async Task OffMenuChoice_GoesToThePolicy_AndThePolicysPickIsVoicedSpeakOnly()
    {
        var chat = new FakeChat(["CHOICE: give_axe_free\nRAPPORT: none\nSAY: Take it, it's yours."], new Queue<string>(["SAY: Nothing more to say, Tam."]));
        var (r, d, l, _, _) = Router(chat);
        var run = r.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        d.Single().Choice.ShouldBeNull();                        // "policy, now"
        d.Single().ProviderTag.ShouldStartWith("guard:off_menu");
        r.OnResolved(7, "none");                                 // the sim's policy picked none
        await run;
        l.Single().Source.ShouldBe("regenerated");
        l.Single().Text.ShouldBe("Nothing more to say, Tam.");
        chat.Requests[^1].Messages[^1].Content.ShouldContain("DECIDED: Bram will do none");
    }

    [Fact]
    public async Task ProseFirst_OrNoModel_ThePolicyDecides_AndTemplatesSpeak()
    {
        var (r, d, l, _, _) = Router(null);
        var run = r.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        d.Single().Choice.ShouldBeNull();
        r.OnResolved(7, "none");
        await run;
        (l.Single().Source, l.Single().Text).ShouldBe(("template", l.Single().Text));
        new[] { "Aye, so it is.", "That it is." }.ShouldContain(l.Single().Text);   // reply.small_talk

        var (r2, d2, l2, _, _) = Router(new FakeChat(["Well now, let me think. CHOICE: none\nSAY: hm"]));
        var run2 = r2.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        d2.Single().ProviderTag.ShouldBe("guard:header");
        r2.OnResolved(7, "invite");
        await run2;
        l2.Single().Source.ShouldBe("regenerated");
    }

    [Fact]
    public async Task TierB_IsVerified_RegeneratedOnce_ThenATemplate()
    {
        var escalation = Dp(9, ("retort", 0.2f, Stakes.Low), ("shove", 0.6f, Stakes.High), ("walk_away", 0.2f, Stakes.Low));
        ReplyRouterTierB(escalation).ShouldBeTrue();

        // Verification passes: the buffered line is revealed whole.
        var (r1, _, l1, p1, _) = Router(new FakeChat(["CHOICE: shove\nRAPPORT: none\nSAY: Get away from me! Out of my forge."]), new FakeVerifier(0.1f));
        await r1.RunAsync(Bundle(escalation, Initiative, "insult"), TestContext.Current.CancellationToken);
        (l1.Single().Source, p1.Count).ShouldBe(("llm", 1));   // nothing streamed before verification

        // It fails, the regeneration fails too: a template voices the decision already made.
        var chat = new FakeChat(["CHOICE: shove\nRAPPORT: none\nSAY: Fine, have it your way, no harm done."], new Queue<string>(["SAY: Sure, all is forgiven."]));
        var (r2, d2, l2, _, _) = Router(chat, new FakeVerifier(0.9f));
        await r2.RunAsync(Bundle(escalation, Initiative, "insult"), TestContext.Current.CancellationToken);
        d2.Single(x => x.Id == 9).Choice.ShouldBe("shove");   // the decision stands; only the words are replaced
        l2.Single().Source.ShouldBe("template");
        l2.Single().Flags.ShouldContain("verify_failed");
        new[] { "Get away from me!", "Enough!" }.ShouldContain(l2.Single().Text.Split(" ")[0] == "Get" ? "Get away from me!" : "Enough!");
    }

    private static bool ReplyRouterTierB(DecisionPointOpened dp) => DialogueReplyRouter.TierB(dp, "retort");

    [Fact]
    public async Task RuleChecks_ANumberNotOnTheOption_TruncatesTheTierAStream()
    {
        var sell = Dp(11, ("counter_step_1", 0.6f, Stakes.Low), ("refuse", 0.4f, Stakes.Low));
        var slots = new Dictionary<string, IReadOnlyDictionary<string, string>> { ["counter_step_1"] = new Dictionary<string, string> { ["price"] = "9 farthings" } };
        var (r, _, l, _, _) = Router(new FakeChat(["CHOICE: counter_step_1\nRAPPORT: none\nSAY: Nine farthings, then. Or seven if you're quick."]));
        await r.RunAsync(Bundle(sell, Initiative, "trade_offer", "Six farthings?", slots), TestContext.Current.CancellationToken);
        l.Single().Text.ShouldBe("Nine farthings, then.");   // "seven" is on no option → cut at the last good sentence
        l.Single().Flags.ShouldContain("truncated:number:7");
    }

    [Fact]
    public async Task NetworkCut_OrHeaderDeadline_ThePolicyDecidesAndTheTurnStillGetsALine()
    {
        var (r, d, l, _, _) = Router(new FakeChat(["CHOICE: none\n"], fail: true));
        var run = r.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        d.Single().Choice.ShouldBeNull();
        r.OnResolved(7, "none");
        await run;
        l.Single().Flags.ShouldContain("provider_error");
        l.Single().Text.Length.ShouldBeGreaterThan(0);

        var slow = new FakeChat(["CH", "OI", "CE", ": none\n"], delay: TimeSpan.FromSeconds(1.5));
        var (r2, d2, l2, _, _) = Router(slow);
        var run2 = r2.RunAsync(Bundle(Initiative), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        d2.Single().Choice.ShouldBeNull();   // no header within 4 s → the policy decides
        r2.OnResolved(7, "none");
        await run2;
        l2.Single().Flags.ShouldContain("header_deadline");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
