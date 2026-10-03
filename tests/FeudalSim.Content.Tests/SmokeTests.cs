namespace FeudalSim.Content.Tests;

public class SmokeTests
{
    [Fact]
    public void Project_loads() => typeof(FeudalSim.Content.AssemblyMarker).Assembly.ShouldNotBeNull();
}
