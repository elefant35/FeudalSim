using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FeudalSim.AI;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;

namespace FeudalSim.AI.Tests;

/// <summary>M1-12: the whole turn against a stepped world — classification → DPs → decision-first reply → logged decisions and line.</summary>
public sealed class DialogueHostTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private sealed class ScriptedChat(Func<ChatRequest, string> reply) : IChatProvider
    {
        public string Tag => "scripted";
        public ConcurrentQueue<ChatRequest> Requests { get; } = new();

        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct)
        {
            Requests.Enqueue(request);
            return Task.FromResult(new ChatResult(reply(request), 10, 10, 50, 0, Tag));
        }

        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Enqueue(request);
            foreach (var part in reply(request).Split(' '))
            {
                await Task.Yield();
                yield return part + " ";
            }
        }
    }

    /// <summary>Steps the world on the test thread like SimRunner does, feeding submitted commands in with fresh Seq numbers.</summary>
    private sealed class Loop
    {
        public required SimWorld W { get; init; }
        public required ConcurrentQueue<(CommandSource, StateCommand)> Inbox { get; init; }
        public DialogueHost? Host { get; set; }
        public List<StepOutput> Outputs { get; } = [];

        public void Step()
        {
            while (Inbox.TryDequeue(out var c)) { W.Enqueue(new CommandEnvelope(W.LastCommandSeq + 1, 0, c.Item1, c.Item2)); }
            var o = W.Step();
            Outputs.Add(o);
            foreach (var dp in o.OpenedDecisions) { W.Enqueue(new CommandEnvelope(W.LastCommandSeq + 1, 0, CommandSource.Integrity, dp)); }
            Host?.OnStep(W, o);
        }

        public void RunUntil(Func<bool> done, int maxSteps = 400)
        {
            for (var i = 0; i < maxSteps && !done(); i++) { Step(); Thread.Sleep(2); }
        }

        public IEnumerable<T> Events<T>() where T : DomainEvent => Outputs.SelectMany(o => o.Events).Select(e => e.Payload).OfType<T>();
    }

    private static (Loop L, DialogueHost Host, int Npc) Start(IChatProvider? chat)
    {
        var scenario = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_talk.yaml"));
        var inbox = new ConcurrentQueue<(CommandSource, StateCommand)>();
        var loop = new Loop { W = scenario.CreateWorld(Content, Sim.SerialJobScheduler.Instance), Inbox = inbox };
        var host = new DialogueHost((s, c) => inbox.Enqueue((s, c)), chat, null, new AiConfig { LlmMode = chat is null ? "template" : "auto", ChatKey = new Secret(chat is null ? null : "test-key") }, Content);
        loop.Host = host;
        for (var i = 0; i < 20; i++) { loop.Step(); }
        const int npc = 4;
        var t = loop.W.People.Transforms[npc];
        inbox.Enqueue((CommandSource.Embodiment, new PlayerMoved(t.X + 1f, t.Z, 0f)));
        loop.Step();
        inbox.Enqueue((CommandSource.Player, new StartConversation(loop.W.People.Ids[npc])));
        loop.RunUntil(() => host.Conversation is not null, 20);
        return (loop, host, npc);
    }

    [Fact]
    public async Task ATurnRoundTrips_TheModelDecidesFirst_TheLineJoinsTheTranscript()
    {
        var chat = new ScriptedChat(r => "CHOICE: none\nRAPPORT: none\nSAY: Evening to you, Tam. Sit if you like.");
        var (loop, host, npc) = Start(chat);
        host.Conversation.ShouldNotBeNull();
        var said = await host.SayAsync("Good evening, friend.", TestContext.Current.CancellationToken);
        said!.Act.ShouldBe("greet_farewell");   // no decider: the heuristic classifies
        loop.RunUntil(() => loop.W.Conversations.Open.Single().Transcript.Count >= 2);

        var resolved = loop.Events<DecisionResolved>().Single(r => r.Owner == Sim.Dialogue.InitiativeOwner.Id);
        (resolved.Chosen, resolved.Decider, resolved.Guard).ShouldBe(("none", DeciderKind.Llm, GuardOutcome.Passed));
        var transcript = loop.W.Conversations.Open.Single().Transcript;
        transcript[^2].ShouldEndWith(": Good evening, friend.");
        transcript[^1].ShouldBe($"{loop.W.People.Names[npc]}: Evening to you, Tam. Sit if you like.");
        var prompt = chat.Requests.First().Messages;
        prompt[0].Content.ShouldContain("RULES - always follow them");
        prompt[1].Content.ShouldContain("PERSONA");
        prompt[^1].Content.ShouldContain("<player_said speaker=\"Tam\">Good evening, friend.</player_said>");
        prompt[^1].Content.ShouldContain("- none:");
    }

    [Fact]
    public async Task TemplateMode_ThePolicyDecides_TemplatesSpeak_TheCampIsPlayable()
    {
        var (loop, host, _) = Start(null);
        (await host.SayAsync("You lazy fool.", TestContext.Current.CancellationToken))!.Act.ShouldBe("insult");
        loop.RunUntil(() => loop.W.Conversations.Open.FirstOrDefault()?.Transcript.Count >= 2 || loop.W.Conversations.Count == 0);
        var escalation = loop.Events<DecisionResolved>().First(r => r.Owner == Sim.Social.EscalationOwner.Id);
        escalation.Decider.ShouldBe(DeciderKind.Policy);
        loop.Events<DecisionResolved>().ShouldAllBe(r => r.Decider == DeciderKind.Policy);
        var lines = loop.Outputs.SelectMany(o => o.AppliedCommands).Select(c => c.Payload).OfType<DialogueLineRendered>().ToList();
        lines.ShouldNotBeEmpty();
        lines[0].Source.ShouldBe("template");
    }

    [Fact]
    public async Task AReadLineIsNotSaid_UntilCommitted_SoUnsayLeavesNothing()
    {
        var (loop, host, npc) = Start(null);
        var pending = await host.ReadAsync("You lazy fool.", TestContext.Current.CancellationToken);
        pending.ShouldNotBeNull();
        (pending.Command.Act, pending.Consequential).ShouldBe(("insult", true));
        pending.Echo.ShouldStartWith("read as: Insult");
        loop.Inbox.ShouldBeEmpty();                       // nothing submitted while the echo waits
        for (var i = 0; i < 50; i++) { loop.Step(); }
        loop.Events<DecisionResolved>().ShouldNotContain(r => r.Owner == Sim.Social.EscalationOwner.Id);   // unsaid: no DP opened

        host.Commit(pending).ShouldBeTrue();
        loop.RunUntil(() => loop.Events<DecisionResolved>().Any(r => r.Owner == Sim.Social.EscalationOwner.Id));
        var chooser = loop.W.People.Ids[npc];
        loop.Events<DecisionResolved>().Any(r => r.Owner == Sim.Social.EscalationOwner.Id && r.Chooser == chooser).ShouldBeTrue();
    }

    [Fact]
    public void QuickIntents_SkipTheClassifier_WithTheNeutralWordsSignal_AndOutcomesShowOnlyTheChoice()
    {
        var (loop, host, npc) = Start(null);
        var outcomes = new List<FeudalSim.Hosting.TurnOutcome>();
        host.Outcome += o => outcomes.Add(o);
        var intent = host.Intent(new FeudalSim.Hosting.QuickIntent("request", "[You ask for a hand with the firewood]", "action.gather_wood", 1f));
        intent.ShouldNotBeNull();
        (intent.Command.Persuasiveness, intent.Command.Politeness, intent.Consequential).ShouldBe((4f, 3f, false));
        host.Commit(intent).ShouldBeTrue();
        loop.RunUntil(() => outcomes.Any(o => o.Owner == Sim.Social.RequestOwner.Id));
        var answer = outcomes.First(o => o.Owner == Sim.Social.RequestOwner.Id);
        answer.Stance.ShouldBe(FeudalSim.Hosting.DialogueTurns.Stance(answer.Chosen));
        answer.Stance.ShouldBeOneOf("agreeable", "bargaining", "unmoved");
        host.Conversation!.Who.ShouldNotBeNullOrEmpty();
        host.Conversation.Cue.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Echo_Downgrade_AndGlyphs_FollowNineteen()
    {
        FeudalSim.Hosting.DialogueTurns.Downgrade("threaten").ShouldBe("tell");
        FeudalSim.Hosting.DialogueTurns.Echo("tell", "", true, "threaten").ShouldBe("read as: Warning (unclear)");
        FeudalSim.Hosting.DialogueTurns.Echo("request", "formal_polite", false, null).ShouldBe("read as: Request · formal polite");
        FeudalSim.Hosting.DialogueTurns.IsConsequential("request").ShouldBeFalse();
        FeudalSim.Hosting.DialogueTurns.Glyph(null, "warm_to_speaker").ShouldBe("▲ warmer");
        var dp = new DecisionPointOpened(1, "16.request", new DpContext("k", new Sim.Core.EntityId(1), new Sim.Core.EntityId(2), 0), 0,
            [new("accept_request", "accept", [], true, 0.1f, 0f, Stakes.Low, true, "help"), new("refuse_request", "refuse", [], true, 0.9f, 0f, Stakes.Low, false, "no")],
            ["accept_request", "refuse_request"], 0, 40, DeciderKind.Llm);
        FeudalSim.Hosting.DialogueTurns.Glyph(dp, "accept_request").ShouldBe("✧ talked round");
        FeudalSim.Hosting.DialogueTurns.Glyph(dp, "refuse_request").ShouldBeNull();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
