using FeudalSim.Headless.Commands;
using Spectre.Console.Cli;

namespace FeudalSim.Headless;

public static class Program
{
    public static int Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("feudalsim");
            config.AddCommand<RunCommand>("run").WithDescription("Run a scenario headless at max speed and write metrics.");
            config.AddBranch("ai", ai =>
            {
                ai.SetDescription("Check the AI gateway (OpenRouter key from .env; never printed).");
                ai.AddCommand<AiPingCommand>("ping").WithDescription("One chat completion from LLM_DIALOGUE_MODEL.");
                ai.AddCommand<AiDecideCommand>("decide").WithDescription("One fast-decider choice question (normalized option probabilities).");
            });
            config.AddBranch("content", content =>
            {
                content.SetDescription("Validate and compile game content (YAML).");
                content.AddCommand<ContentValidateCommand>("validate").WithDescription("Parse, schema-check, validate and compile all content.");
                content.AddCommand<ContentSchemasCommand>("schemas").WithDescription("Regenerate JSON Schemas from the C# definition types (--check to verify they are fresh).");
            });
        });
        return app.Run(args);
    }
}
