namespace FeudalSim.Sim.Systems;

/// <summary>10 §6.3: advances the region's weather at each 6-hour slot boundary.</summary>
public sealed class WeatherSystem : ISimSystem
{
    public string Name => "Weather";

    public SimPhase Phase => SimPhase.Sense;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var slot = ctx.GameMinute / Climate.Weather.SlotMinutes;
        ref var w = ref world.WeatherRef;
        if (w.YearRolled != 0 && w.Slot >= slot) { return; }
        for (var s = w.YearRolled == 0 ? slot : w.Slot + 1; s <= slot; s++) { Climate.Weather.Advance(ref w, world.WorldSeed, s); }
    }
}
