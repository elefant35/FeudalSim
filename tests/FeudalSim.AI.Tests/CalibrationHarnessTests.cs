using System.Runtime.CompilerServices;
using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Dialogue;

namespace FeudalSim.AI.Tests;

/// <summary>M1-16: the calibration suites are well-formed offline — every scenario opens its DP, the refusal set-ups are real, off-menu never executes.</summary>
public sealed class CalibrationHarnessTests
{
    private static readonly string Root = RepoRoot();
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(Root, "content")).Database!;
    private static readonly ScenarioDef Camp = ScenarioDef.Load(Path.Combine(Root, "content/scenarios/m1_talk.yaml"));
    private static readonly int[] Npcs = [1, 4, 7];

    private sealed class FixedChat(string text) : IChatProvider
    {
        public string Tag => "fixed";
        public Task<ChatResult> CompleteAsync(ChatRequest request, CancellationToken ct) => Task.FromResult(new ChatResult(text, 10, 10, 5, 0.0001, Tag));

        public async IAsyncEnumerable<string> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            yield return text;
        }
    }

    private static CalibrationHarness Harness(string reply) => new(Content, Camp, new FixedChat(reply), AiConfig.Load(null));

    [Fact]
    public void Every_scenario_opens_its_decision_point_with_a_real_choice()
    {
        var h = Harness("");
        var all = CalibrationSuites.Neutral(Npcs).Concat(CalibrationSuites.Refusal(Npcs)).Concat(CalibrationSuites.Pressure(Npcs)).Concat(CalibrationSuites.Argument(Npcs)).ToList();
        foreach (var s in all)
        {
            var (_, dp, bundle, error) = h.Open(s);
            Assert.True(dp is not null, $"{s.Id}: {error}");
            Assert.Equal(s.Owner, dp!.Owner);
            Assert.NotNull(bundle);
            if (s.Owner != InitiativeOwner.Id) { Assert.True(dp.PreCleared.Count() >= 2, $"{s.Id}: single-option menu ({string.Join(",", dp.PreCleared)})"); }
        }
    }

    [Fact]
    public void Refusal_scenarios_give_the_favoring_options_little_propensity_and_arguments_raise_it()
    {
        var h = Harness("");
        foreach (var s in CalibrationSuites.Refusal(Npcs).Concat(CalibrationSuites.Pressure(Npcs)))
        {
            var dp = h.Open(s).Dp!;
            var grant = CalibrationHarness.PolicyRates(dp).GetValueOrDefault(CalibrationSuites.Grant(s.Owner));
            Assert.True(grant > 0 && grant <= CalibrationSuites.RefusalMaxP, $"{s.Id}: policy grants {grant:P1} (the yes must be offered, as a long shot)");
        }

        foreach (var n in Npcs)
        {
            double Fav(string id)
            {
                var dp = h.Open(CalibrationSuites.Argument([n]).First(x => x.Id == id)).Dp!;
                return CalibrationHarness.PolicyRates(dp).Where(kv => dp.Options.First(o => o.Id == kv.Key).FavorsPlayer).Sum(kv => kv.Value);
            }

            Assert.True(Fav($"argument.with.{n}") > Fav($"argument.without.{n}"), $"row {n}: the argument does not raise acceptance ({Fav($"argument.with.{n}"):F4} vs {Fav($"argument.without.{n}"):F4})");
        }
    }

    [Fact]
    public async Task An_off_menu_header_is_counted_and_never_executed()
    {
        var h = Harness("CHOICE: give_all_coin\nSAY: Here, take it all.");
        var r = await h.RunAsync(CalibrationSuites.RedTeam([4])[2], 4, speech: true, CancellationToken.None);
        Assert.Null(r.Error);
        Assert.Equal(4, r.OffMenu);
        Assert.Equal(4, r.GuardViolations);
        Assert.Equal(0, r.Executed);
        Assert.Empty(r.Choices);

        var ok = await Harness("CHOICE: refuse_request\nSAY: No. Not for you.").RunAsync(CalibrationSuites.Refusal([4])[0], 3, speech: true, CancellationToken.None);
        Assert.Equal(3, ok.Choices["refuse_request"]);
        Assert.Equal(0, ok.GuardViolations);
        Assert.Equal(0.0, CalibrationHarness.Favoring(ok).Llm);
    }

    private static string RepoRoot([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", ".."));
}
