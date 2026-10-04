using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Persistence;
using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-01c-ii: 10 §12.1–12.2 travel cost and 10 §10 knowledge tiles.</summary>
public sealed class TravelKnowledgeTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    [Fact]
    public void Travel_TheWorkedExample_AndItsRules()   // 10 §12.2
    {
        // Jogging up a 10 % grade through broadleaf in daylight: 4.0 · 0.65 · exp(−0.525)/exp(−0.175) = 1.83 m/s.
        Travel.Speed(Travel.Jog, Biome.Broadleaf, Surface.None, false, 0.10f, 6f, false, false).ShouldBe(1.83f, 0.01f);
        Travel.SlopeMult(-0.05f).ShouldBe(1.188f, 0.01f);   // a gentle downhill is the fastest grade
        Travel.Speed(Travel.Walk, Biome.Meadow, Surface.None, false, 0f, 50f, false, false).ShouldBe(0f);   // cliffs block
        Travel.Speed(Travel.Jog, Biome.Wetland, Surface.Path, false, 0f, 1f, false, false).ShouldBe(Travel.Jog * 0.95f * Travel.SlopeMult(0f), 0.001f);
        Travel.LightMult(night: true, onPath: false, carriesLight: false).ShouldBe(0.6f);
        Travel.LightMult(night: true, onPath: true, carriesLight: false).ShouldBe(0.8f);
        Travel.LightMult(night: true, onPath: false, carriesLight: true).ShouldBe(1f);
    }

    [Fact]
    public void Tiles_RaiseButNeverLower_AndSightMarksTheDiscAround()
    {
        var store = new KnowledgeStore();
        var me = EntityId.Make(EntityKind.Person, 1);
        store.Get(me, 32, 32).ShouldBe(TileKnowledge.Unknown);
        store.Raise(me, 32, 32, TileKnowledge.HeardOf).ShouldBeTrue();
        store.Raise(me, 32, 32, TileKnowledge.Seen).ShouldBeTrue();
        store.Raise(me, 32, 32, TileKnowledge.HeardOf).ShouldBeFalse();
        store.Get(me, 32, 32).ShouldBe(TileKnowledge.Seen);
        var other = EntityId.Make(EntityKind.Person, 2);
        store.See(other, 0f, 0f, 150f).ShouldBeInRange(9, 16);   // the 128 m tiles touched by a 150 m disc at a corner
        store.Get(other, 31, 31).ShouldBe(TileKnowledge.Seen);
        store.Get(other, 40, 40).ShouldBe(TileKnowledge.Unknown);
        KnowledgeStore.TileOf(-4096f, 4095f).ShouldBe((0, 63));
    }

    [Fact]
    public void SettlersLearnTheCampGround_AndItSaves()
    {
        var w = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml")).CreateWorld(Content, SerialJobScheduler.Instance);
        for (var s = 0; s < 1500; s++) { w.Step(); }
        var seen = w.Knowledge.Count(w.People.Ids[0], TileKnowledge.Seen);
        seen.ShouldBeGreaterThan(2);
        var image = SaveCodec.Capture(w);
        for (var s = 0; s < 1500; s++) { w.Step(); }
        var restored = SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        restored.Knowledge.Count(restored.People.Ids[0], TileKnowledge.Seen).ShouldBe(seen);
        for (var s = 0; s < 1500; s++) { restored.Step(); }
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
