namespace FeudalSim.Sim;

/// <summary>
/// Brackets each system's run with its registration index, so a host can time systems (<c>feudalsim bench</c>, 20 §19).
/// Must not change state; the sim itself never reads a clock.
/// </summary>
public interface ISystemObserver
{
    void Begin(int systemIndex);

    void End(int systemIndex);
}
