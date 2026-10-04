using FeudalSim.Content;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-01a: deterministic person generation (canon §10, 21 §4, 12 §3.2/§5.4/§8.5) from real content.</summary>
public sealed class PersonGenerationTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Spawn(ulong seed, int n, string culture = "culture.varrow", int age = 0, string? profession = null)
    {
        var w = new SimWorld(seed) { Content = Content };
        for (var i = 0; i < n; i++) { w.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"P{i}", 0, 0, culture, profession, age))); }
        w.Step();
        return w;
    }

    private static int T(string id) => Content.TraitHandle(id);

    [Fact]
    public void Generation_IsAPureFunctionOfSeedAndId()
    {
        StateHasher.Hash(Spawn(11, 50)).ShouldBe(StateHasher.Hash(Spawn(11, 50)));
        StateHasher.Hash(Spawn(11, 50)).ShouldNotBe(StateHasher.Hash(Spawn(12, 50)));
        var a = Spawn(11, 50).People;
        var b = Spawn(11, 50).People;
        a.Personality.ToArray().ShouldBe(b.Personality.ToArray());
        a.SkillLevelsAll.ToArray().ShouldBe(b.SkillLevelsAll.ToArray());
    }

    [Fact]
    public void Facets_TraitCounts_AndIncompatibilities_FollowCanon()
    {
        var p = Spawn(3, 3000).People;
        var facets = p.Personality.ToArray().SelectMany(x => new[] { x.Curiosity, x.Diligence, x.Sociability, x.Warmth, x.Volatility }).Select(b => (double)b).ToArray();
        facets.Average().ShouldBe(50, 1.0);
        Math.Sqrt(facets.Average(f => (f - 50) * (f - 50))).ShouldBe(15, 1.0);   // SD 15 (clamping trims a little)

        var counts = p.Personality.ToArray().Select(x => System.Numerics.BitOperations.PopCount(x.Traits)).ToArray();
        counts.ShouldAllBe(c => c >= 2 && c <= 4);
        (counts.Count(c => c == 2) / 3000.0).ShouldBe(0.35, 0.03);
        (counts.Count(c => c == 3) / 3000.0).ShouldBe(0.45, 0.03);
        (counts.Count(c => c == 4) / 3000.0).ShouldBe(0.20, 0.03);

        foreach (var x in p.Personality.ToArray())
        {
            for (var t = 0; t < Content.Traits.Count; t++)
            {
                if (!x.HasTrait(t)) { continue; }
                foreach (var other in Content.Traits[t].Incompatible ?? []) { x.HasTrait(T(other)).ShouldBeFalse($"{Content.Traits[t].Id} with {other}"); }
            }
        }
    }

    [Fact]
    public void TraitValueRules_Culture_AndFacetAffinity_ShapeTheResult()
    {
        var varrow = Spawn(5, 3000).People.Personality.ToArray();
        var brannoch = Spawn(5, 3000, "culture.brannoch").People.Personality.ToArray();
        var ashen = Spawn(5, 3000, "culture.ashen_reform").People.Personality.ToArray();

        varrow.Where(x => x.HasTrait(T("trait.pious"))).ShouldAllBe(x => x.Values.Faith >= 70);
        varrow.Where(x => x.HasTrait(T("trait.skeptic"))).ShouldAllBe(x => x.Values.Faith <= 30);
        varrow.Average(x => (double)x.Values.Tradition).ShouldBe(60, 1.5);            // Varrow Tradition mean 60
        ashen.Average(x => (double)x.Values.Tradition).ShouldBe(35, 1.5);             // Ashen Reform Tradition mean 35
        brannoch.Average(x => (double)x.Values.Honor).ShouldBe(65, 1.5);

        static double Share(Personality[] ps, int t) => ps.Count(x => x.HasTrait(t)) / (double)ps.Length;
        Share(brannoch, T("trait.brave")).ShouldBeGreaterThan(Share(varrow, T("trait.brave")) * 1.3);    // Brannoch: brave ×1.6
        Share(ashen, T("trait.pious")).ShouldBeGreaterThan(Share(varrow, T("trait.pious")) * 1.3);       // Ashen: pious ×2.0 vs Varrow ×1.2

        var volatile_ = varrow.Where(x => x.Volatility >= 65).ToArray();
        var calm = varrow.Where(x => x.Volatility <= 35).ToArray();
        Share(volatile_, T("trait.hot_tempered")).ShouldBeGreaterThan(Share(calm, T("trait.hot_tempered")) * 2);   // +0.6·z_Vol
    }

    [Fact]
    public void Skills_FollowTheHomelandTradeRules_AndAptitudeIsHidden0_5To1_5()
    {
        var w = Spawn(9, 400, age: 30, profession: "profession.smith");
        var smithing = Content.SkillHandle("skill.smithing");
        var metallurgy = Content.SkillHandle("skill.metallurgy");
        var farming = Content.SkillHandle("skill.farming");
        var letters = Content.SkillHandle("skill.letters");
        var primary = new List<int>();
        for (var row = 0; row < w.People.Count; row++)
        {
            var s = w.People.SkillLevels(row);
            s[smithing].ShouldBeInRange((byte)15, (byte)65);
            s[metallurgy].ShouldBeInRange((byte)10, (byte)30);   // secondary U(10, 30)
            s[farming].ShouldBeInRange((byte)5, (byte)30);       // common U(5, 20), or U(10, 30) if drawn as the second secondary
            // Everything else U(0, 8) — except one: the smith trade lists one secondary, and 12 §8.5 gives every adult two,
            // so the generator draws the second at random.
            var common = new[] { "skill.athletics", "skill.cooking", "skill.farming", "skill.foraging", "skill.woodcutting" }.Select(Content.SkillHandle);
            var levels = s.ToArray();
            var others = Enumerable.Range(0, PersonTable.SkillCount).Except(common).Except([smithing, metallurgy]).Count(k => levels[k] > 8);
            others.ShouldBeLessThanOrEqualTo(1);
            primary.Add(s[smithing]);
            w.People.Personality[row].Profession.ShouldBe((ushort)Content.ProfessionHandle("profession.smith"));
            foreach (var a in w.People.SkillAptitude(row)) { a.ShouldBeInRange((byte)50, (byte)150); }
        }

        primary.Average().ShouldBe(22 + (0.9 * 12), 1.5);   // 22 + 0.9·(age − 18)
        w.People.SkillAptitudeAll.ToArray().Average(b => (double)b).ShouldBe(100, 1.5);

        var children = Spawn(9, 50, age: 8);
        for (var row = 0; row < children.People.Count; row++)
        {
            children.People.Personality[row].Profession.ShouldBe(Personality.None);
            var s = children.People.SkillLevels(row);
            s[farming].ShouldBeLessThanOrEqualTo((byte)10);
            s[letters].ShouldBe((byte)0);
        }
    }

    [Fact]
    public void Spawn_SetsAgeFromBirth_AndRejectsUnknownProfessions()
    {
        var w = Spawn(2, 1, age: 40);
        ((w.Clock.GameMinute - w.People.Core[0].BirthGameMinute) / Sim.Time.GameDate.MinutesPerYear).ShouldBe(40);
        var bad = new SimWorld(2) { Content = Content };
        bad.Enqueue(new CommandEnvelope(1, 0, CommandSource.Scenario, new SpawnPerson("X", 0, 0, Profession: "profession.astronaut")));
        bad.Step().Events.Select(e => e.Payload).OfType<CommandRejected>().ShouldHaveSingleItem();
        bad.People.Count.ShouldBe(0);
    }

    [Fact]
    public void TraitCatalog_HasThe45_With16Canon_AndSymmetricIncompatibility()
    {
        Content.Traits.Count.ShouldBe(45);
        Content.Traits.Count(t => t.Canon).ShouldBe(16);
        string[] canon = ["hot_tempered", "greedy", "honest", "gossip", "coward", "brave", "pious", "vengeful", "lazy", "drunkard", "romantic", "paranoid", "charitable", "ambitious", "stubborn", "jealous"];
        canon.ShouldAllBe(c => Content.Traits.Any(t => t.Id == "trait." + c && t.Canon));
        foreach (var t in Content.Traits)
        {
            foreach (var other in t.Incompatible ?? []) { Content.Traits[T(other)].Incompatible!.ShouldContain(t.Id); }
        }

        Content.Traits[T("trait.coward")].Incompatible!.ShouldContain("trait.reckless");
        Content.Traits[T("trait.reckless")].Incompatible!.ShouldContain("trait.coward");
        Content.Cultures.Select(c => c.Id).ShouldBe(["culture.ashen_reform", "culture.brannoch", "culture.osmeri", "culture.varrow"]);
        Content.Professions.Count.ShouldBe(34);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
