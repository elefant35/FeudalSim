namespace FeudalSim.AI.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_loads() => typeof(FeudalSim.AI.AssemblyMarker).Assembly.ShouldNotBeNull();
}
