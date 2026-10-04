using FeudalSim.Content;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Decisions;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-03: the DP propensity layer reproduces 21 §7.8's and §8.3's worked numbers.</summary>
public sealed class PropensityTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;
    private static readonly HashSet<string> BaseModelsCharacter = ["personality", "emotion", "need", "opinion"];

    /// <summary>Bram Tull (21 §7.7): Diligence 66, Volatility 72, Sociability 45, Warmth 40; Hot-tempered, Proud, Industrious.</summary>
    private static SimWorld Bram()
    {
        var w = new SimWorld(1) { Content = Content };
        w.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("Bram Tull", 0, 0, Profession: "profession.smith", AgeYears: 41)));
        w.Step();
        ref var p = ref w.People.Personality[0];
        (p.Curiosity, p.Diligence, p.Sociability, p.Warmth, p.Volatility) = (50, 66, 45, 40, 72);
        p.Traits = new[] { "trait.hot_tempered", "trait.proud", "trait.industrious" }.Aggregate(0UL, (b, t) => b | (1UL << Content.TraitHandle(t)));
        w.People.Mood[0] = default;
        w.People.Emotions[0] = default;
        return w;
    }

    private static readonly PropensityInput[] HelpMenu =
    [
        new() { Id = "accept_now", Family = "accept", ScheduleFit = -0.5f, AbandonsCurrent = true },   // off-block, abandons the nails
        new() { Id = "accept_after_work", Family = "accept", ScheduleFit = 0f },
        new() { Id = "refuse", Family = "refuse" },
    ];

    private static FamilyBase[] Bases(int repetition = 1) =>
    [
        new() { Family = "accept", Mass = 0.44f, Includes = BaseModelsCharacter, Repetition = repetition },   // 16's Willingness
        new() { Family = "refuse", Mass = 0.56f, Includes = BaseModelsCharacter },
    ];

    [Fact]
    public void BramsHelpRequest_MatchesThe21WorkedExample()
    {
        var r = Propensity.Compute(Bram(), 0, HelpMenu, Bases());
        r.T.ShouldBe(1.25f, 0.01f);
        r.P[0].ShouldBe(0.18f, 0.01f);   // accept_now
        r.P[1].ShouldBe(0.27f, 0.01f);   // accept_after_work
        r.P[2].ShouldBe(0.55f, 0.01f);   // refuse
        r.P.Sum().ShouldBe(1f, 1e-4f);
        r.KeyFactors.ShouldContain(f => f.Contains("temperament"));
        r.KeyFactors.ShouldContain(f => f.StartsWith("accept_now: schedule"));
    }

    [Fact]
    public void TheDramaKnobSharpensAndFlattens_FamiliesNotVariants()
    {
        var calm = Propensity.Compute(Bram(), 0, HelpMenu, Bases(), kIrr: 0f);
        (calm.P[0] + calm.P[1]).ShouldBe(0.28f, 0.01f);   // K_irr 0 → 0.28 / 0.72
        var dramatic = Propensity.Compute(Bram(), 0, HelpMenu, Bases(), kIrr: 2f);
        (dramatic.P[0] + dramatic.P[1]).ShouldBe(0.48f, 0.01f);   // K_irr 2 → 0.48 / 0.52

        // Splitting a family into more variants does not inflate it.
        PropensityInput[] split = [.. HelpMenu, new() { Id = "accept_tomorrow", Family = "accept", ScheduleFit = 0f }];
        var r = Propensity.Compute(Bram(), 0, split, Bases());
        (r.P[0] + r.P[1] + r.P[3]).ShouldBe(0.45f, 0.01f);
    }

    [Fact]
    public void AskingAgainTheSameDay_HalvesTheAcceptMass()
    {
        var again = Propensity.Compute(Bram(), 0, HelpMenu, Bases(repetition: 2));
        (again.P[0] + again.P[1]).ShouldBe(0.32f, 0.01f);   // 21 §7.8: 0.22 → p_accept 0.32
    }

    [Fact]
    public void AngerAboveThreshold_FoldsIntoTheOutlet()
    {
        var w = Bram();
        w.People.Emotions[0].Anger = 76;   // Hot-tempered θ 55, z_Vol 1.47 → P_hijack 0.31 (21 §8.3 example)
        PropensityInput[] menu =
        [
            new() { Id = "laugh_it_off", Family = "de-escalate" },
            new() { Id = "retort", Family = "escalate" },
            new() { Id = "confront", Family = "escalate", Outlet = "anger" },
        ];
        FamilyBase[] bases = [new() { Family = "de-escalate", Mass = 0.5f, Includes = BaseModelsCharacter }, new() { Family = "escalate", Mass = 0.5f, Includes = BaseModelsCharacter }];
        var calm = Propensity.Compute(Bram(), 0, menu, bases);
        var angry = Propensity.Compute(w, 0, menu, bases);
        var pH = angry.P[2] - calm.P[2];
        ((angry.P[2] - calm.P[2]) / (1 - calm.P[2])).ShouldBe(0.31f, 0.01f);   // p_outlet ← P_h + (1 − P_h)·p_outlet
        angry.P.Sum().ShouldBe(1f, 1e-4f);
        angry.KeyFactors[0].ShouldContain("anger boiling over");
        pH.ShouldBeGreaterThan(0.2f);

        // If the base already models anger (16's escalation ladder does), no fold-in.
        FamilyBase[] ladder = [.. bases.Select(b => b with { Includes = new HashSet<string>(BaseModelsCharacter) { "anger" } })];
        Propensity.Compute(w, 0, menu, ladder).P[2].ShouldBe(calm.P[2], 1e-4f);
    }

    [Fact]
    public void CharacterTermsApplyWhenTheBaseDoesNotModelThem()
    {
        var w = Bram();
        PropensityInput[] menu =
        [
            new() { Id = "help", Family = "accept", UtilityKey = "help", Facet = ("warmth", 0.30f), Direction = 1 },
            new() { Id = "refuse", Family = "refuse", UtilityKey = "refuse" },
        ];
        FamilyBase[] neutral = [new() { Family = "accept", Mass = 0.5f }, new() { Family = "refuse", Mass = 0.5f }];
        var cold = Propensity.Compute(w, 0, menu, neutral, opinionOfSpeaker: -60);
        var warm = Propensity.Compute(w, 0, menu, neutral, opinionOfSpeaker: 60);
        warm.P[0].ShouldBeGreaterThan(cold.P[0] + 0.1f);   // R(o) = 1 + 0.5·dir·Opinion/100
        cold.P[0].ShouldBeLessThan(0.5f);                    // Warmth 40 (z −0.67): help ×0.80
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
