using System.Reflection;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Tests;

public class RngTests
{
    private static readonly EntityId Bram = EntityId.Make(EntityKind.Person, 7);

    [Fact]
    public void Same_key_gives_the_same_sequence()
    {
        var a = SimRandom.For(42, 1_000, RngStream.Ai, Bram, Salt.WanderTarget);
        var b = SimRandom.For(42, 1_000, RngStream.Ai, Bram, Salt.WanderTarget);
        for (var i = 0; i < 100; i++) { a.NextUInt().ShouldBe(b.NextUInt()); }
    }

    [Fact]
    public void Each_key_component_changes_the_draw()
    {
        var baseline = SimRandom.For(42, 1_000, RngStream.Ai, Bram, Salt.WanderTarget).NextUInt();
        SimRandom.For(43, 1_000, RngStream.Ai, Bram, Salt.WanderTarget).NextUInt().ShouldNotBe(baseline);
        SimRandom.For(42, 1_001, RngStream.Ai, Bram, Salt.WanderTarget).NextUInt().ShouldNotBe(baseline);
        SimRandom.For(42, 1_000, RngStream.Social, Bram, Salt.WanderTarget).NextUInt().ShouldNotBe(baseline);
        SimRandom.For(42, 1_000, RngStream.Ai, EntityId.Make(EntityKind.Person, 8), Salt.WanderTarget).NextUInt().ShouldNotBe(baseline);
        SimRandom.For(42, 1_000, RngStream.Ai, Bram, Salt.WanderPause).NextUInt().ShouldNotBe(baseline);
    }

    [Fact]
    public void Range_and_float_stay_in_bounds_and_cover_the_range()
    {
        var rng = SimRandom.For(1, 1, RngStream.Scenario, Bram, Salt.ScenarioSpawn);
        var seen = new bool[10];
        for (var i = 0; i < 10_000; i++)
        {
            var r = rng.Range(0, 10);
            r.ShouldBeInRange(0, 9);
            seen[r] = true;
            var f = rng.NextFloat01();
            (f >= 0f && f < 1f).ShouldBeTrue();
        }

        seen.ShouldAllBe(x => x);
    }

    [Fact]
    public void Salts_are_unique()
    {
        var salts = typeof(Salt).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral).Select(f => (Name: f.Name, Value: (uint)f.GetRawConstantValue()!)).ToArray();
        salts.Length.ShouldBeGreaterThan(0);
        var duplicates = salts.GroupBy(s => s.Value).Where(g => g.Count() > 1).Select(g => string.Join("/", g.Select(s => s.Name))).ToArray();
        duplicates.ShouldBeEmpty();
    }

    [Fact]
    public void Entity_ids_encode_kind_and_never_repeat()
    {
        var alloc = new EntityIdAllocator();
        var a = alloc.Next(EntityKind.Person);
        var b = alloc.Next(EntityKind.Person);
        var h = alloc.Next(EntityKind.Household);
        a.Kind.ShouldBe(EntityKind.Person);
        h.Kind.ShouldBe(EntityKind.Household);
        b.CompareTo(a).ShouldBePositive();
        new[] { a, b, h }.Distinct().Count().ShouldBe(3);
    }
}
