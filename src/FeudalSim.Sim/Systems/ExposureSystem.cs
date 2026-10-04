using FeudalSim.Sim.Survival;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Systems;

/// <summary>
/// 11 §9 exposure for the camp (M2-05a): each due person's thermal balance from the weather, wind, the camp's shelter
/// and fire, their clothing, wetness and activity; then warmth, wetness and hypothermia. Hypothermia's Downed and death
/// thresholds are M2-06's (incapacitation); here the 50 and 80 crossings raise an event. LOD3 rows: Lod3System.
/// </summary>
public sealed class ExposureSystem : ISimSystem
{
    private WearValues[] _wear = [];
    private ulong _contentHash;

    public string Name => "Exposure";

    public SimPhase Phase => SimPhase.Resolve;

    public void Run(in StepContext ctx, SimWorld world)
    {
        if (world.Camp.Active == 0) { return; }
        if (_contentHash != world.Content.Hash || _wear.Length != world.Content.Items.Count) { (_wear, _contentHash) = (WearValues.For(world.Content), world.Content.Hash); }
        var people = world.People;
        var due = world.Due;
        ref readonly var camp = ref world.Camp;
        ref readonly var weather = ref world.WeatherRef;
        var air = Climate.Weather.AirTempC(weather, ctx.GameMinute, camp.ElevationM, camp.Coastal != 0);
        var fireLit = camp.FireFuelMin > 0f;
        for (var k = 0; k < due.Count; k++)
        {
            var i = due.Rows[k];
            if (people.Lod[i].Tier == LodTier.Lod3) { continue; }
            var dtH = due.Dt(k) / (float)Time.SimClock.MsPerGameHour;
            var x = Inputs(world, i, air, fireLit);
            ref var body = ref people.Body[i];
            ref var n = ref people.Needs[i];
            var ins = Exposure.Insulation(people.Worn[i], _wear, body.Wetness);
            var c = Exposure.CoreC(x, ins);
            body.Wetness = Exposure.StepWetness(body.Wetness, x, Exposure.RainResist(people.Worn[i], _wear), c, dtH);
            n.Warmth = Exposure.StepWarmth(n.Warmth, c, x.FireDistM <= 3f, x.Vulnerable, dtH);
            var before = body.Hypothermia;
            body.Hypothermia = Exposure.StepHypothermia(before, n.Warmth, c, dtH);
            foreach (var mark in Marks)
            {
                if (before < mark && body.Hypothermia >= mark) { world.Emit(Events.Salience.Notable, people.Ids[i], new Events.HypothermiaRose(people.Ids[i], (int)mark)); }
            }
        }
    }

    private static ReadOnlySpan<float> Marks => [50f, 80f];

    /// <summary>The exposure inputs for one person this step (shared with tests and the client readout).</summary>
    public static ExposureInputs Inputs(SimWorld world, int row, float airC, bool fireLit)
    {
        var people = world.People;
        ref readonly var camp = ref world.Camp;
        ref readonly var t = ref people.Transforms[row];
        var dShelter = MathF.Sqrt(((t.X - camp.ShelterX) * (t.X - camp.ShelterX)) + ((t.Z - camp.ShelterZ) * (t.Z - camp.ShelterZ)));
        var dFire = fireLit ? MathF.Sqrt(((t.X - camp.FireX) * (t.X - camp.FireX)) + ((t.Z - camp.FireZ) * (t.Z - camp.FireZ))) : float.MaxValue;
        var sheltered = dShelter <= 4f;
        var level = people.Activity[row].Level;
        var age = (world.Clock.GameMinute - people.Core[row].BirthGameMinute) / Time.GameDate.MinutesPerYear;
        ref readonly var w = ref world.WeatherRef;
        return new ExposureInputs(airC, w.WindMs, w.Sky, sheltered ? camp.ShelterWindBlock : 0f, sheltered ? camp.ShelterRainBlock : 0f,
            sheltered ? camp.ShelterInsulation : 0f, dFire, level, level == Content.ActivityLevel.Sleep ? camp.BeddingInsulation : 0f, age < 14 || age >= 65);
    }
}
