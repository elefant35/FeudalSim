using FeudalSim.Sim.Events;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// 11 §10.4 spoilage (M2-07b-ii): once a game hour every food stack ages by tempF / shelf life, with tempF =
/// clamp(2^((T − 10)/10), 0.25, 3) from the air at the camp (frozen at ≤ −2 °C: 0.05). Everything is in the open (piles,
/// sacks: containerF 1) until 14's stores exist; quality and pest loss wait for them too. Rotten stacks are removed with a
/// <see cref="FoodRotted"/> event.
/// </summary>
public sealed class SpoilageSystem : ISimSystem
{
    private readonly List<(ulong Container, int Item, int Qty)> _rotted = [];

    public string Name => "Spoilage";

    public SimPhase Phase => SimPhase.World;

    public static float TempFactor(float airC) => airC <= -2f ? 0.05f : Math.Clamp(MathF.Pow(2f, (airC - 10f) / 10f), 0.25f, 3f);

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (ctx.GameMinute / 60 == (ctx.GameMs - ctx.DtGameMs) / Time.SimClock.MsPerGameHour) { return; }
        var air = Climate.Weather.AirTempC(world.WeatherRef, ctx.GameMinute, world.Camp.ElevationM, world.Camp.Coastal != 0);
        world.Inventory.AgeFood(world.Content, TempFactor(air), 1f, _rotted);
        foreach (var (container, item, qty) in _rotted) { world.Emit(Salience.Minor, new Core.EntityId(container), new FoodRotted(new Core.EntityId(container), item, qty)); }
    }
}
