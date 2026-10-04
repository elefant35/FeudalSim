namespace FeudalSim.AI;

/// <summary>
/// Builds the gateway's provider chain from config, the same way for the realtime runner and the AI CLI:
/// HTTP → <see cref="TranscriptHandler"/> (AI_GATEWAY_MODE) → chat provider and fast decider. Never prints keys.
/// </summary>
public sealed class AiStack : IDisposable
{
    private AiStack(AiConfig config, HttpClient http, OpenAiCompatibleChatProvider? chat, IDecider? decider)
    {
        Config = config;
        Http = http;
        Chat = chat;
        Decider = decider;
    }

    public AiConfig Config { get; }
    public HttpClient Http { get; }
    public OpenAiCompatibleChatProvider? Chat { get; }
    public IDecider? Decider { get; }

    /// <summary><paramref name="inner"/> replaces the network (tests); default is a real socket handler.</summary>
    public static AiStack Create(AiConfig config, HttpMessageHandler? inner = null)
    {
        var handler = new TranscriptHandler(config.GatewayMode, config.LogTranscripts, config.TranscriptDir, inner ?? new SocketsHttpHandler());
        var http = new HttpClient(handler, disposeHandler: inner is null) { Timeout = TimeSpan.FromSeconds(30) };
        if (config.TemplateMode) { return new AiStack(config, http, null, null); }

        // Replay never sends a request, but the provider still builds an Authorization header.
        var chatKey = config.ChatKey.IsSet ? config.ChatKey : new Secret("replay-without-key");
        var chat = new OpenAiCompatibleChatProvider(http, config.ChatBaseUrl, chatKey);
        IDecider decider = config.DeciderProvider switch
        {
            "heuristic" => new HeuristicDecider(),
            _ => new LogprobChoiceDecider(
                new OpenAiCompatibleChatProvider(http, config.DeciderBaseUrl, config.DeciderKey.IsSet ? config.DeciderKey : chatKey), config.DeciderModel),
        };
        return new AiStack(config, http, chat, decider);
    }

    public AiGateway CreateGateway(TimeSpan? timeout = null, Func<long>? clockMs = null)
        => new(Config, Chat, Decider, timeout, clockMs);

    public void Dispose() => Http.Dispose();
}
