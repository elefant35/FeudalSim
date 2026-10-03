using System.Net;
using System.Text;
using FeudalSim.AI;
using FeudalSim.Headless.Commands;

namespace FeudalSim.AI.Tests;

/// <summary>M0-10: the AI CLI never leaks keys and degrades to template/heuristic without one.</summary>
[Collection("AiCli")]   // static test hooks: run serially
public class AiCliTests
{
    private const string FakeKey = "sk-or-test-SECRET-7f3a9c";

    private sealed class FakeHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        public List<string> AuthHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            AuthHeaders.Add(request.Headers.Authorization?.ToString() ?? "");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(request), Encoding.UTF8, "application/json") });
        }
    }

    private const string ChatBody = """{"choices":[{"message":{"content":"Well met, stranger."},"finish_reason":"stop"}],"usage":{"prompt_tokens":50,"completion_tokens":5,"cost":0.00001}}""";
    private const string DecideBody = """{"choices":[{"message":{"content":"B"},"logprobs":{"content":[{"token":"B","logprob":-0.2,"top_logprobs":[{"token":"B","logprob":-0.2},{"token":"C","logprob":-1.8},{"token":"A","logprob":-6.0},{"token":"D","logprob":-7.0}]}]}}],"usage":{"prompt_tokens":120}}""";

    private static AiConfig WithKey(string key) => new() { ChatKey = new Secret(key), DeciderKey = new Secret(key) };

    [Fact]
    public async Task Ping_and_decide_use_the_key_but_never_print_it()
    {
        var chat = new FakeHandler(_ => ChatBody);
        var decide = new FakeHandler(_ => DecideBody);
        AiCli.ConfigFactory = () => WithKey(FakeKey);
        AiPingCommand.TestHandler = chat;
        AiDecideCommand.TestHandler = decide;
        try
        {
            var output = new StringWriter();
            (await AiPingCommand.Run(output, TestContext.Current.CancellationToken)).ShouldBe(0);
            (await AiDecideCommand.Run(output, TestContext.Current.CancellationToken)).ShouldBe(0);
            var text = output.ToString();

            text.ShouldContain("key: set");
            text.ShouldContain("reply: Well met, stranger.");
            text.ShouldContain("sum: 1.00 · chosen: B");
            text.ShouldNotContain("SECRET");                               // the scan
            chat.AuthHeaders.ShouldAllBe(h => h == $"Bearer {FakeKey}");    // …but it was used
            decide.AuthHeaders.ShouldAllBe(h => h == $"Bearer {FakeKey}");
            new Secret(FakeKey).ToString().ShouldBe("***");
        }
        finally
        {
            AiCli.ConfigFactory = null;
            AiPingCommand.TestHandler = null;
            AiDecideCommand.TestHandler = null;
        }
    }

    [Fact]
    public async Task Without_a_key_ping_falls_back_to_template_and_decide_to_heuristic()
    {
        AiCli.ConfigFactory = () => new AiConfig();
        try
        {
            var output = new StringWriter();
            (await AiPingCommand.Run(output, TestContext.Current.CancellationToken)).ShouldBe(0);
            (await AiDecideCommand.Run(output, TestContext.Current.CancellationToken)).ShouldBe(0);
            var text = output.ToString();
            text.ShouldContain("key: missing");
            text.ShouldContain("fallback: template");
            text.ShouldContain("fallback: heuristic");
            text.ShouldContain("sum: 1.00");
        }
        finally
        {
            AiCli.ConfigFactory = null;
        }
    }

    [Fact]
    public async Task A_model_without_log_probabilities_is_reported_unavailable()
    {
        AiCli.ConfigFactory = () => WithKey(FakeKey);
        AiDecideCommand.TestHandler = new FakeHandler(_ => """{"choices":[{"message":{"content":"B"}}]}""");
        try
        {
            var output = new StringWriter();
            (await AiDecideCommand.Run(output, TestContext.Current.CancellationToken)).ShouldBe(0);
            output.ToString().ShouldContain("decider: unavailable (no log-probabilities returned)");
        }
        finally
        {
            AiCli.ConfigFactory = null;
            AiDecideCommand.TestHandler = null;
        }
    }

    [Fact]
    public void Dotenv_parsing_handles_quotes_comments_and_export()
    {
        var parsed = DotEnv.Parse(["# comment", "export A=1", "B=\"two words\"", "C='3'", "BAD", "D="]).ToDictionary(x => x.Key, x => x.Value);
        parsed["A"].ShouldBe("1");
        parsed["B"].ShouldBe("two words");
        parsed["C"].ShouldBe("3");
        parsed["D"].ShouldBe("");
        parsed.ContainsKey("BAD").ShouldBeFalse();
    }
}
