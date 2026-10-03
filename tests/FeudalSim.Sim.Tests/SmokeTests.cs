namespace FeudalSim.Sim.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_loads() => typeof(FeudalSim.Sim.AssemblyMarker).Assembly.ShouldNotBeNull();
}
