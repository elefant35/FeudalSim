using System.Net;
using System.Text;
using FeudalSim.AI;

namespace FeudalSim.AI.Tests;

/// <summary>Spike S2 bench: SSE parsing, decision-header timing markers and validation (22 §4.8).</summary>
public sealed class DialogueBenchTests
{
    private sealed class Sse(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") });
    }

    private static string Chunk(string? content = null, string? reasoning = null, string? usage = null)
        => "data: {\"provider\":\"Fireworks\",\"choices\":[{\"delta\":{" +
           string.Join(",", new[] { content is null ? null : $"\"content\":{System.Text.Json.JsonSerializer.Serialize(content)}", reasoning is null ? null : $"\"reasoning\":{System.Text.Json.JsonSerializer.Serialize(reasoning)}" }.Where(x => x is not null)) +
           "}}]" + (usage is null ? "" : $",\"usage\":{usage}") + "}\n\n";

    private static async Task<BenchTurn> Run(string sse)
    {
        using var http = new HttpClient(new Sse(sse));
        var chat = new OpenAiCompatibleChatProvider(http, "https://openrouter.ai/api/v1", new Secret("k"));
        return await DialogueBench.RunTurnAsync(chat, "m", "default", 0, "sys", "user", ["refuse", "counter_step_1"], ["warm_to_speaker"], null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ParsesHeaderSayUsageAndProvider_AcrossChunkBoundaries()
    {
        var r = await Run(Chunk(reasoning: "hmm") + Chunk("CHO") + Chunk("ICE: counter_st") + Chunk("ep_1\nRAPPORT: warm_to_speaker\nSA") + Chunk("Y: Forty-two, ") +
                          Chunk("lad. Iron costs.") + Chunk(content: "", usage: "{\"prompt_tokens\":2100,\"completion_tokens\":24,\"cost\":0.000258}") + "data: [DONE]\n\n");
        (r.Choice, r.Rapport, r.Say, r.HeaderValid, r.Provider).ShouldBe(("counter_step_1", "warm_to_speaker", "Forty-two, lad. Iron costs.", true, "Fireworks"));
        (r.TokensIn, r.TokensOut, r.CostUsd, r.ThinkLeak, r.Error).ShouldBe((2100, 24, 0.000258, true, (string?)null));
        r.FirstContentMs.ShouldBeGreaterThanOrEqualTo(r.FirstTokenMs);
        r.ChoiceMs.ShouldBeGreaterThanOrEqualTo(r.FirstContentMs);
        r.FirstWordsMs.ShouldBeGreaterThanOrEqualTo(r.ChoiceMs);
    }

    [Theory]
    [InlineData("Well now.\nCHOICE: refuse\nRAPPORT: none\nSAY: No.")]   // prose before the header
    [InlineData("CHOICE: give_it_away\nRAPPORT: none\nSAY: Take it.")]  // off-menu id
    [InlineData("CHOICE: refuse\nRAPPORT: adore_speaker\nSAY: No.")]    // unknown rapport id
    [InlineData("CHOICE: refuse\nRAPPORT: none\n")]                       // no speech
    public async Task MalformedHeaders_AreInvalid(string output)
        => (await Run(Chunk(output) + "data: [DONE]\n\n")).HeaderValid.ShouldBeFalse();
}
