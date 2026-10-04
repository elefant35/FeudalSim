using FeudalSim.AI.Dialogue;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;

namespace FeudalSim.AI.Tests;

/// <summary>M1-13: prompts built from sim state (22 §7.1–7.4) and template coverage for every line kind (canon §13.5).</summary>
public sealed class PromptAndTemplateTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static readonly Dictionary<string, string> AllSlots = new()
    {
        ["player"] = "Tam", ["npc"] = "Bram", ["price"] = "3 shillings", ["item"] = "iron axe", ["task"] = "gather firewood", ["claim"] = "Hild stole from Osk", ["reason"] = "I need to sleep",
    };

    [Fact]
    public void EveryOptionAndEveryAct_HasATemplate_ThatRendersCompletely()
    {
        var bank = new TemplateBank(Content);
        var options = Content.Decisions.SelectMany(d => d.Options.Select(o => o.Id)).Distinct().ToList();
        options.Count.ShouldBeGreaterThan(40);
        foreach (var id in options.Concat(TurnClassifier.Acts.Select(a => $"reply.{a.Id}")))
        {
            bank.Has(id).ShouldBeTrue(id);
            for (ulong k = 0; k < 4; k++)
            {
                var line = bank.Render(id, k, AllSlots);
                line.ShouldNotContain("{");
                line.Length.ShouldBeGreaterThan(0);
            }
        }
    }

    [Theory]
    [InlineData("Eleven pence and a farthing, no less.", 45, true)]
    [InlineData("11 pennies and 1 farthing.", 45, true)]
    [InlineData("Fine. Forty-five farthings.", 45, true)]
    [InlineData("Fine, take it for forty.", 45, false)]
    [InlineData("All right, it's yours.", 45, false)]
    public void PriceMustBeSaid(string line, long price, bool ok)
        => SpeechChecks.CarriesPrice(line, new Dictionary<string, string> { ["price_f"] = price.ToString(System.Globalization.CultureInfo.InvariantCulture), ["price"] = "11 pennies and 1 farthing" }).ShouldBe(ok);

    [Fact]
    public void PersonaAndPrompt_AreBandedWords_WithinThe22Budget()
    {
        var scenario = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_talk.yaml"));
        var w = scenario.CreateWorld(Content, SerialJobScheduler.Instance);
        for (var i = 0; i < 30; i++) { w.Step(); }
        var t = w.People.Transforms[4];
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Embodiment, new PlayerMoved(t.X + 1, t.Z, 0)));
        w.Step();
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new StartConversation(w.People.Ids[4])));
        w.Step();
        var conv = w.Conversations.Open.Single();
        w.Enqueue(new CommandEnvelope(w.LastCommandSeq + 1, 0, CommandSource.Player, new PlayerUtteranceClassified(conv.Id, 1, "insult", 0.9f, 0f, "You lazy fool.")));
        var o = w.Step();

        var bundle = TurnBundleBuilder.Build(w, conv, o.OpenedDecisions);
        bundle.Primary.Owner.ShouldBe(Sim.Social.EscalationOwner.Id);   // the response DP leads; the initiative rides beside it
        bundle.Initiative!.Owner.ShouldBe(Sim.Dialogue.InitiativeOwner.Id);
        var facts = bundle.Facts;
        facts.Persona.ShouldContain("Temperament:");
        facts.Persona.ShouldContain("Cares most about:");
        var traits = Enumerable.Range(0, Content.Traits.Count).Where(h => w.People.Personality[4].HasTrait(h)).Select(h => Content.Traits[h].VoicePhrase!).ToList();
        traits.ShouldAllBe(v => facts.Persona.Contains(v));                            // trait voice phrases, not trait ids
        facts.Relationship.ShouldStartWith("TOWARD TAM:");
        new[] { "hates", "dislikes", "no strong feeling", "likes", "fond" }.ShouldContain(x => facts.Relationship.Contains(x));
        facts.Now.ShouldContain("FEELS:");
        facts.Now.ShouldContain("at Tam");                                             // anger at the speaker, in words
        System.Text.RegularExpressions.Regex.Replace(facts.Relationship + facts.Now, @"Settler \d+", "X").ShouldNotMatch(@"\d{2,}");   // no raw scalars (22 §7.4); placeholder names aside

        var messages = PromptBuilder.DecisionFirst(bundle);
        var tokens = messages.Sum(m => m.Content.Length) / 4.0;   // ≈ 4 characters a token for English
        tokens.ShouldBeLessThan(2_290 * 1.1);                     // 22 §7.1's budget
        messages[^1].Content.ShouldContain("INITIATIVE (optional");
        messages[^1].Content.ShouldNotMatch(@"\(0\.\d+\)");       // inclinations are words, never p_i
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
