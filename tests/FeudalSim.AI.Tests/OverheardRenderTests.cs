using FeudalSim.AI;

namespace FeudalSim.AI.Tests;

/// <summary>M1-07c: the 22 §9.2 post-generation check on overheard talk (rules + speaker check).</summary>
public sealed class OverheardRenderTests
{
    private const string Facts = """
        {"setting":"a landfall camp","place":"at the fire","time":"evening","kind":"gossip","success":true,
         "a":{"name":"Settler 1","trade":"Smith","temperament":"warm"},"b":{"name":"Settler 2","trade":"Fisher","temperament":"reserved"},
         "claim":"Settler 7 stole from Settler 3","claim_about":"Settler 7","listener_choice":"doubts it",
         "names":["Settler 1","Settler 2","Settler 7","Settler 3"]}
        """;

    private static string Lines(params (string Speaker, string Line)[] lines)
        => "[" + string.Join(",", lines.Select(l => $$"""{"speaker":"{{l.Speaker}}","line":"{{l.Line}}"}""")) + "]";

    [Fact]
    public void AFaithfulExchange_Passes_AndIsNormalized()
    {
        var reply = "```json\n" + Lines(("Settler 1", "They say Settler 7 took grain from Settler 3."), ("Settler 2", "I'll believe it when I see it.")) + "\n```";
        var ok = OverheardRender.Validate(Facts, reply, out var reason);
        reason.ShouldBe("ok");
        ok.ShouldBe("""[{"speaker":"Settler 1","line":"They say Settler 7 took grain from Settler 3."},{"speaker":"Settler 2","line":"I'll believe it when I see it."}]""");
    }

    [Theory]
    [InlineData("not json at all", "no-json-array")]
    [InlineData("""[{"speaker":"Settler 1","line":"Settler 7 stole, they say."}]""", "line-count:1")]
    [InlineData("""[{"speaker":"Settler 9","line":"Settler 7 stole."},{"speaker":"Settler 2","line":"No."}]""", "unknown-speaker")]
    [InlineData("""[{"speaker":"Settler 1","line":"Settler 7 and Settler 12 stole."},{"speaker":"Settler 2","line":"No."}]""", "invented-name")]
    [InlineData("""[{"speaker":"Settler 1","line":"Have you heard the news?"},{"speaker":"Settler 2","line":"No."}]""", "claim-not-voiced")]
    [InlineData("""[{"speaker":"Settler 1","line":""},{"speaker":"Settler 2","line":"No."}]""", "empty-line")]
    public void ARepliesThatBreaksTheFacts_IsRefused(string reply, string expected)
    {
        OverheardRender.Validate(Facts, reply, out var reason).ShouldBeNull();
        reason.ShouldBe(expected);
    }

    [Fact]
    public void ThePromptIsSpeakOnly_AndCarriesTheFactsVerbatim()
    {
        var messages = OverheardRender.Messages(Facts);
        messages[0].Role.ShouldBe("system");
        messages[0].Content.ShouldContain("only voice it");
        messages[1].Content.ShouldBe(Facts);
    }
}
