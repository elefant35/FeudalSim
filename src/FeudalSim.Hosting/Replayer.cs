using FeudalSim.Sim;
using FeudalSim.Sim.Commands;

namespace FeudalSim.Hosting;

/// <summary>
/// Replays an input log headless (20 §8.8): each logged command is enqueued so it applies at its recorded
/// <c>ApplyStep</c>. No network, no timing — AI results and rejections reproduce exactly.
/// </summary>
public static class Replayer
{
    public static void Run(SimWorld world, IReadOnlyList<CommandEnvelope> log, long untilStep, Action<StepOutput>? onStep = null)
    {
        var next = 0;
        while (world.Clock.Step < untilStep)
        {
            var stepToApply = world.Clock.Step + 1;
            while (next < log.Count && log[next].ApplyStep == stepToApply)
            {
                world.Enqueue(log[next] with { ApplyStep = 0 });
                next++;
            }

            if (next < log.Count && log[next].ApplyStep < stepToApply)
            {
                throw new InvalidDataException($"Log out of order at Seq {log[next].Seq} (ApplyStep {log[next].ApplyStep} < {stepToApply}).");
            }

            onStep?.Invoke(world.Step());
        }
    }
}
