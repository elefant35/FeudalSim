using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using FeudalSim.AI;
using FeudalSim.AI.Dialogue;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Events;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public sealed class TalkSettings : CommandSettings
{
    [CommandOption("--scenario <PATH>")]
    public string Scenario { get; init; } = "content/scenarios/m1_talk.yaml";

    [CommandOption("--npc <ROW>")]
    [Description("Row of the settler to talk to (default 4).")]
    public int Npc { get; init; } = 4;

    [CommandOption("--say <TEXT>")]
    [Description("A line the player says (repeat for several turns).")]
    public string[] Say { get; init; } = ["Good evening. Hard day at the woodpile?", "I could use a hand gathering firewood tomorrow, if you're willing.", "You're a lazy fool and everyone knows it."];

    [CommandOption("--log <PATH>")]
    [Description("Write the session's input log (replay it with `feudalsim replay`).")]
    public string? Log { get; init; }

    [CommandOption("--turn-timeout <S>")]
    public double TurnTimeout { get; init; } = 12;
}

/// <summary>
/// A live conversation through the whole M1 turn pipeline (22 §4): the real-time runner, the gateway, the dialogue host.
/// Prints per turn: classification (act, latency), the decision gesture (DecisionSurfaced), first words, the final line and
/// its source, the sim's DecisionResolved records; then timing percentiles and dialogue spend.
/// </summary>
public sealed class TalkCommand : Command<TalkSettings>
{
    public override int Execute(CommandContext context, TalkSettings settings, CancellationToken cancellationToken)
    {
        var content = ContentCompiler.Compile(RepoPaths.FindContentRoot(Directory.GetCurrentDirectory())).Database!;
        var scenario = ScenarioDef.Load(settings.Scenario);
        var config = AiConfig.Load(AiConfig.FindEnvFile(Directory.GetCurrentDirectory()));
        Console.WriteLine($"talk: key {(config.ChatKey.IsSet ? "set" : "missing")}, mode {(config.TemplateMode ? "template" : "live")}, gateway {config.GatewayMode}, dialogue {config.DialogueModel}, decider {config.DeciderModel}");
        using var jobs = new JobRunner(1);
        var world = scenario.CreateWorld(content, jobs);
        using var stack = AiStack.Create(config);
        using var gateway = stack.CreateGateway();
        using var log = settings.Log is null ? null : InputLogFile.OpenOrCreate(settings.Log);
        using var runner = new SimRunner(world, log, RunMode.Running, gateway);
        var host = new DialogueHost(runner.Submit, stack.Chat, stack.Decider, config, content);
        runner.Dialogue = host;
        var clock = Stopwatch.StartNew();
        double turnAt = 0;
        double? gesture = null, firstWords = null, final = null;
        var finalText = "";
        host.Surfaced += s => gesture ??= clock.Elapsed.TotalMilliseconds - turnAt;
        host.Partial += p =>
        {
            firstWords ??= clock.Elapsed.TotalMilliseconds - turnAt;
            if (p.Final) { (final, finalText) = (clock.Elapsed.TotalMilliseconds - turnAt, p.Text); }
        };

        Thread.Sleep(1_500);   // spawns applied
        var (npcId, npcName, pos) = runner.Invoke(w => (w.People.Ids[settings.Npc], w.People.Names[settings.Npc], w.People.Transforms[settings.Npc])).GetAwaiter().GetResult();
        runner.Submit(CommandSource.Embodiment, new PlayerMoved(pos.X + 1f, pos.Z, 0f));
        Thread.Sleep(300);
        runner.Submit(CommandSource.Player, new StartConversation(npcId));
        for (var i = 0; i < 30 && host.Conversation is null; i++) { Thread.Sleep(100); }
        if (host.Conversation is null) { Console.WriteLine("talk: no conversation (asleep or too far?)"); return 1; }
        Console.WriteLine($"talk: talking to {npcName}");

        var rows = new List<(double Classify, double? Gesture, double? Words, double? Final)>();
        foreach (var line in settings.Say)
        {
            if (host.Conversation is null) { Console.WriteLine("talk: the conversation has ended"); break; }
            (gesture, firstWords, final, finalText) = (null, null, null, "");
            turnAt = clock.Elapsed.TotalMilliseconds;
            var c = host.SayAsync(line, cancellationToken).GetAwaiter().GetResult();
            var classify = clock.Elapsed.TotalMilliseconds - turnAt;
            Console.WriteLine($"\n> {line}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  classified {c?.Act} p {c?.ActP:0.00}{(c?.Ambiguous == true ? " (ambiguous)" : "")} injection {c?.InjectionP:0.00} in {classify:F0} ms ({c?.Provider})"));
            var deadline = clock.Elapsed.TotalSeconds + settings.TurnTimeout;
            while (final is null && clock.Elapsed.TotalSeconds < deadline) { Thread.Sleep(50); }
            while (runner.Events.TryPop(out var e))
            {
                if (e.Payload is DecisionResolved r && r.Chooser == npcId) { Console.WriteLine($"  {r.Owner}: {r.Chosen} by {r.Decider} ({r.Guard}, {r.ProviderTag})"); }
                if (e.Payload is ConversationEnded end) { Console.WriteLine($"  conversation ended: {end.Reason}"); }
                if (e.Payload is ConfrontationEscalated or FightResolved or RequestAnswered or InitiativeTaken) { Console.WriteLine($"  {e.Payload}"); }
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {npcName}: \"{finalText}\""));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  gesture {gesture:F0} ms · first words {firstWords:F0} ms · line {final:F0} ms (from the player's line, incl. classification)"));
            rows.Add((classify, gesture, firstWords, final));
            Thread.Sleep(2_100);   // the 2 s turn limit
        }

        runner.Pause();
        Thread.Sleep(300);
        var (endStep, endHash) = runner.Invoke(w => (w.Clock.Step, FeudalSim.Sim.StateHasher.Hash(w))).GetAwaiter().GetResult();
        Console.WriteLine($"talk: final step {endStep} hash {endHash:x16}{(settings.Log is null ? "" : $" · log {settings.Log}")}");
        double P50(IEnumerable<double?> xs) { var v = xs.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToArray(); return v.Length == 0 ? double.NaN : v[v.Length / 2]; }
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"\ntalk: {rows.Count} turns · p50 classify {P50(rows.Select(r => (double?)r.Classify)):F0} ms · gesture {P50(rows.Select(r => r.Gesture)):F0} ms · first words {P50(rows.Select(r => r.Words)):F0} ms · line {P50(rows.Select(r => r.Final)):F0} ms · dialogue spend ${host.SpentUsd:F4} (+ gateway ${gateway.SpentUsd:F4})"));
        return 0;
    }
}
