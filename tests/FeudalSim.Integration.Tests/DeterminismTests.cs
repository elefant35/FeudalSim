using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Integration.Tests;

/// <summary>M0-05 (20 §20 step 5): the determinism harness.</summary>
public class DeterminismTests
{
    private static ulong Run(ulong seed, int steps, int threads, ISimSystem? extra = null, int people = 300)
    {
        using var jobs = new JobRunner(threads);
        var world = new SimWorld(seed) { Jobs = jobs }
            .AddSystem(new WanderSystem())
            .AddSystem(new NeedsDecaySystem());
        if (extra is not null) { world.AddSystem(extra); }
        for (var i = 0; i < people; i++)
        {
            world.Enqueue(new CommandEnvelope(i + 1, 0, CommandSource.Scenario, new SpawnPerson($"P{i}", i % 20 * 5f, i / 20 * -5f)));
        }

        for (var i = 0; i < steps; i++) { world.Step(); }
        return StateHasher.Hash(world);
    }

    [Fact]
    public void Same_seed_twice_gives_identical_hashes_over_10000_steps()
        => Run(42, 10_000, threads: 1).ShouldBe(Run(42, 10_000, threads: 1));

    [Fact]
    public void One_thread_and_four_threads_give_identical_hashes()
        => Run(42, 10_000, threads: 4).ShouldBe(Run(42, 10_000, threads: 1));

    [Fact]
    public void Different_seeds_give_different_hashes()
        => Run(42, 2_000, threads: 1).ShouldNotBe(Run(43, 2_000, threads: 1));

    [Fact]
    public void Injected_hash_dependent_ordering_is_caught_by_a_hash_mismatch()
    {
        // .NET's Dictionary/HashSet enumerate in insertion order until items are removed, so the
        // realistic hazard is ordering that depends on hash codes (identity hashes, per-process
        // string hashing, removal-reordered sets). This system picks a row by identity hash code;
        // two otherwise identical runs must disagree, and StateHasher must notice.
        var a = Run(42, 500, threads: 1, extra: new HashOrderBugSystem());
        var b = Run(42, 500, threads: 1, extra: new HashOrderBugSystem());
        a.ShouldNotBe(b);
    }

    private sealed class HashOrderBugSystem : ISimSystem
    {
        public string Name => "HashOrderBug";
        public SimPhase Phase => SimPhase.World;

        public void Run(in StepContext ctx, SimWorld world)
        {
            var rows = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            for (var i = 0; i < world.People.Count; i++) { rows[new object()] = i; }

            var first = rows.MinBy(kv => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(kv.Key)).Value;
            world.People.Needs[first].Comfort -= 0.01f;
        }
    }
}
