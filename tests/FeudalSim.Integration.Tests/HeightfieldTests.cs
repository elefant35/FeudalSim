using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M1-S4: the heightfield is generated in fixed chunks in parallel and is identical to the serial result.</summary>
public sealed class HeightfieldTests
{
    [Fact]
    public void ParallelGeneration_IsBitIdenticalToSerial()
    {
        var serial = Heightfield.Generate(42, 769, 2f);
        using var jobs = new JobRunner(4);
        var parallel = Heightfield.Generate(42, 769, 2f, jobs: jobs);
        parallel.Length.ShouldBe(serial.Length);
        for (var i = 0; i < serial.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(serial[i]) != BitConverter.SingleToInt32Bits(parallel[i])) { throw new ShouldAssertException($"height {i} differs"); }
        }

        Heightfield.Generate(42, 769, 2f, jobs: SerialJobScheduler.Instance)[12345].ShouldBe(serial[12345]);
    }
}
