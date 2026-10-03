namespace FeudalSim.Sim;

/// <summary>Phases of one fine step (20 §7.1). Systems run in phase order, then registration order.</summary>
public enum SimPhase : byte { Sense = 2, Decide = 3, Resolve = 4, World = 5 }

public interface ISimSystem
{
    string Name { get; }
    SimPhase Phase { get; }
    void Run(in StepContext ctx, SimWorld world);
}
