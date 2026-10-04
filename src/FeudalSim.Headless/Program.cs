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
            config.AddCommand<SweepCommand>("sweep").WithDescription("Run a scenario over many seeds and check 21 §19 camp metrics against their bands.");
            config.AddCommand<RumorCommand>("rumor").WithDescription("Seed one claim with 3 witnesses and measure its spread (16 §7.8 propagation speeds).");
            config.AddCommand<ReplayCommand>("replay").WithDescription("Replay a recorded input log headless and print the final state hash.");
            config.AddCommand<BenchCommand>("bench").WithDescription("Time a scenario per step and per system against the 20 §19 budgets (spike S6).");
            config.AddCommand<RunCommand>("run").WithDescription("Run a scenario headless at max speed and write metrics.");
            config.AddBranch("ai", ai =>
            {
                ai.SetDescription("Check the AI gateway (OpenRouter key from .env; never printed).");
                ai.AddCommand<AiPingCommand>("ping").WithDescription("One chat completion from LLM_DIALOGUE_MODEL.");
                ai.AddCommand<AiDecideCommand>("decide").WithDescription("One fast-decider choice question (normalized option probabilities).");
                ai.AddCommand<AiBenchDialogueCommand>("bench-dialogue").WithDescription("Spike S2: stream decision-first dialogue turns per model; latency percentiles and cost per play-hour.");
                ai.AddCommand<AiBenchDeciderCommand>("bench-decider").WithDescription("Spike S3: fast-decider bake-off on the golden set (accuracy, ECE, injection, latency, fan-out, cost).");
            });
            config.AddBranch("log", log =>
            {
                log.SetDescription("Inspect input and event logs (.fslog).");
                log.AddCommand<LogInputsCommand>("inputs").WithDescription("Print logged commands.");
                log.AddCommand<LogEventsCommand>("events").WithDescription("Print logged domain events.");
            });
            config.AddBranch("content", content =>
            {
                content.SetDescription("Validate and compile game content (YAML).");
                content.AddCommand<ContentValidateCommand>("validate").WithDescription("Parse, schema-check, validate and compile all content.");
                content.AddCommand<ContentLicensesCommand>("licenses").WithDescription("Generate ASSET_LICENSES.md from the asset manifest (--check to verify).");
                content.AddCommand<ContentSchemasCommand>("schemas").WithDescription("Regenerate JSON Schemas from the C# definition types (--check to verify they are fresh).");
            });
        });
        return app.Run(args);
    }
}
