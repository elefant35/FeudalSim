using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.Time;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-06: memories (16 §6) and the NPC↔NPC interaction policy (16 §5).</summary>
public sealed class InteractionTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));
    private static readonly EntityId A = new(1), B = new(2), C = new(3);
    private const long Day = 1440;

    [Fact]
    public void Salience_HalvesAfterItsHalfLife_AndCoreMemoriesKeepAFloor()
    {
        var m = new MemoryStore();
        m.Remember(A, MemoryKind.Help, B, A, 0, baseKind: 50, relevance: 1f, intensity: 0f, valence: 50);   // S0 50 → h = 2 + 15 = 17 d
        MemoryStore.Salience(m.Of(A)[0], 17 * Day).ShouldBe(25f, 0.05f);

        m.Remember(A, MemoryKind.Assault, B, A, 0, baseKind: 60, relevance: 1f, intensity: 50f, valence: -90);   // S0 90, core
        m.Of(A)[1].Core.ShouldBeTrue();
        MemoryStore.Salience(m.Of(A)[1], 1000 * Day).ShouldBe(45f, 0.01f);   // floor 0.5·S0

        m.Remember(A, MemoryKind.Chat, B, A, 0, baseKind: 4, relevance: 1f, intensity: 0f, valence: 10);   // S0 4 < 5: not stored
        m.Of(A).Count.ShouldBe(2);
    }

    [Fact]
    public void Compaction_DropsFaintMemories_AndMergesRepeatsIntoPatterns()
    {
        var m = new MemoryStore();
        m.Remember(A, MemoryKind.Chat, C, A, 0, 5, 1f, 0f, 10);   // S0 5, h 3.5 d: below 3 after ~2.6 d
        for (var d = 0; d < 10; d++)
        {
            if (d < 4) { m.Remember(A, MemoryKind.Chat, B, A, (d * Day) + 600, 5, 1f, 10f, 10); }   // S0 6, h 3.8 d
            m.Compact((d * Day) + 1380);   // nightly, as SocialSystem runs it
        }

        var left = m.Of(A);
        left.ShouldHaveSingleItem();   // the lone chat with C faded below 3; the four with B merged
        left[0].Actor.ShouldBe(B.Value);
        left[0].Count.ShouldBe((ushort)4);
        left[0].S0.ShouldBeGreaterThan((byte)10);   // max salience + 5·log2(4)
    }

    [Fact]
    public void Enemy_NeedsAGraveMemory_NotJustLowOpinion()
    {
        var w = new SimWorld(1, startGameMs: 0) { Content = Content };
        w.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("Ada", 0, 0)));
        w.Enqueue(new CommandEnvelope(2, 0, CommandSource.Scenario, new SpawnPerson("Bors", 1, 0)));
        w.Step();
        EntityId a = w.People.Ids[0], b = w.People.Ids[1];
        for (var k = 0; k < 4; k++) { w.Relationships.ApplyModifier(a, b, "opinion.insulted_me", isPublic: true); }
        w.Relationships.Opinion(a, b).ShouldBeLessThanOrEqualTo(-30f);

        // Push opinion to ≤ −50 with stacked insults but no memory: not an enemy (16 §4.12).
        foreach (var mod in new[] { "opinion.humiliated_me", "opinion.humiliated_me", "opinion.threatened_me", "opinion.argued_with_me", "opinion.mocked_me" })
        {
            for (var k = 0; k < 4; k++) { w.Relationships.ApplyModifier(a, b, mod, isPublic: true); }
        }

        w.Relationships.Opinion(a, b).ShouldBeLessThanOrEqualTo(-50f);
        w.Relationships.DailyUpdate();
        w.Relationships.TryGet(a, b, out var edge).ShouldBeTrue();
        (edge.Tags & RelTags.Enemy).ShouldBe(RelTags.None);

        // A grave memory (salience ≥ 50, valence ≤ −50) about Bors makes it stick.
        w.Memories.Remember(a, MemoryKind.Assault, b, a, w.Clock.GameMinute, 60, 1f, 0f, -80);
        w.Relationships.DailyUpdate();
        (edge.Tags & RelTags.Enemy).ShouldBe(RelTags.Enemy);
    }

    [Fact]
    public void TheCamp_TalksAtThe16Budget_FormsFriendships_AndStaysDeterministic()
    {
        static (SimWorld W, InteractionSystem I) Run()
        {
            var w = Camp.CreateWorld(Content, SerialJobScheduler.Instance);
            var target = 20 * Day * SimClock.MsPerGameMinute;
            while (w.Clock.GameMs < target) { w.Step(); }
            return (w, w.Systems.OfType<InteractionSystem>().Single());
        }

        var (w, ix) = Run();
        var perPersonDay = ix.Counts.Sum() / (double)(w.People.Count * 20);
        perPersonDay.ShouldBeInRange(3.5, 7.0);   // 16 §5.1: I ≈ 5.1 initiations per waking day
        var chat = ix.Counts[(int)InteractionSystem.Kind.Chat];
        chat.ShouldBeGreaterThan(ix.Counts[(int)InteractionSystem.Kind.Joke]);
        ix.Counts[(int)InteractionSystem.Kind.Request].ShouldBeGreaterThan(0);
        w.Memories.Count.ShouldBeGreaterThan(0);
        w.Memories.Of(w.People.Ids[0]).Count.ShouldBeLessThanOrEqualTo(MemoryStore.Cap);
        w.Relationships.Edges.Count(e => e.Value.InteractionsToday > 4).ShouldBe(0);   // ≤ 4 per pair per day

        StateHasher.Hash(Run().W).ShouldBe(StateHasher.Hash(w));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
