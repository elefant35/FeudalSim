using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Sim.Survival;

/// <summary>
/// 11 §11 water (M2-07c): source contamination c_src by kind, §7.4's taint (a corpse at the water place: +0.3 for 3 days),
/// Flux exposure per 0.5 L drunk (c_src × 0.04, boiled ×0.02), whether a settler bothers to boil (a pot, a lit fire and
/// their Diligence; the folk belief "bad water brings the flux" is held by all), the player's own drinking at a brook,
/// a lake or the sea (brackish: +5 then −15, net −10), and §7.4's background Flux from poor sanitation.
/// </summary>
public static class Water
{
    /// <summary>One drink: 0.5 L, +20 Hydration (11 §11.2).</summary>
    public const float DrinkHydration = 20f, ReachM = 4f, TaintM = 30f;

    public static float SourceContamination(string kind) => kind switch
    {
        "spring" or "rain" => 0f, "well" => 0.01f, "river" => 0.04f, "lake" => 0.05f, "marsh" => 0.30f, _ => 0.02f,
    };

    /// <summary>The source kind of fresh water at a point on the map (a stream or brook, a river, a lake shore, a spring), or null.</summary>
    public static string? KindAt(WorldGrid g, float x, float z)
    {
        var half = (g.Size - 1) * g.CellM / 2f;
        int c = (int)MathF.Round((x + half) / g.CellM), r = (int)MathF.Round((z + half) / g.CellM);
        if (c < 1 || r < 1 || c >= g.Size - 1 || r >= g.Size - 1) { return null; }
        var best = (string?)null;
        for (var dr = -1; dr <= 1; dr++)
        {
            for (var dc = -1; dc <= 1; dc++)
            {
                switch ((WaterClass)g.Water[((r + dr) * g.Size) + c + dc])
                {
                    case WaterClass.Spring: return "spring";
                    case WaterClass.Lake: best = "lake"; break;
                    case WaterClass.River: best ??= "river"; break;
                    case WaterClass.Stream: best ??= "stream"; break;
                }
            }
        }

        return best;
    }

    /// <summary>The camp water's c_src now: the source plus §7.4's taint.</summary>
    public static float CampContamination(SimWorld world)
        => world.Camp.WaterContamination + (world.Clock.GameMinute < world.Camp.WaterTaintUntilMin ? 0.3f : 0f);

    /// <summary>Whether this person boils what they drink at the camp: a pot, a lit fire, and p = 0.2 + 0.6 × Diligence/100.</summary>
    public static bool Boils(SimWorld world, int row, ulong salt)
    {
        if (world.Camp.Pots == 0 || world.Camp.FireFuelMin <= 0f) { return false; }
        var p = 0.2f + (0.6f * world.People.Personality[row].Diligence / 100f);
        return new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, world.People.Ids[row].Value, salt, Salt.WaterExposure ^ 0xB011)).Chance(p);
    }

    /// <summary>Flux exposure for <paramref name="hydrationGain"/> drunk at contamination <paramref name="c"/> (boiled ×0.02).</summary>
    public static void Exposure(SimWorld world, int row, float hydrationGain, float c, bool boiled, ulong salt)
    {
        if (c <= 0f || hydrationGain <= 0f) { return; }
        var flux = world.Content.DiseaseHandle("disease.flux");
        if (flux < 0) { return; }
        var per = c * 0.04f * (boiled ? 0.02f : 1f);
        var p = 1f - MathF.Pow(1f - per, hydrationGain / DrinkHydration);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, world.People.Ids[row].Value, salt, Salt.WaterExposure));
        if (rng.Chance(p)) { Health.Conditions.Infect(world, row, flux, salt); }
    }

    /// <summary>§7.4: someone died within 30 m of the camp's water: it is tainted for 3 days.</summary>
    public static void OnDeath(SimWorld world, int row)
    {
        if (world.Camp.Active == 0) { return; }
        ref readonly var t = ref world.People.Transforms[row];
        float dx = t.X - world.Camp.WaterX, dz = t.Z - world.Camp.WaterZ;
        if ((dx * dx) + (dz * dz) <= TaintM * TaintM) { world.Camp.WaterTaintUntilMin = world.Clock.GameMinute + (3 * 1440); }
    }

    /// <summary>
    /// The player's (or anyone's) own drink: at the camp's water place, at fresh water on the map within reach, or the sea.
    /// Returns why not, or null.
    /// </summary>
    public static string? Drink(SimWorld world, int row)
    {
        if (!world.CanAct(row)) { return "can't drink now"; }
        ref var n = ref world.People.Needs[row];
        ref readonly var t = ref world.People.Transforms[row];
        var now = (ulong)world.Clock.GameMinute;
        string source;
        float c;
        float dx = t.X - world.Camp.WaterX, dz = t.Z - world.Camp.WaterZ;
        if (world.Camp.Active != 0 && (dx * dx) + (dz * dz) <= ReachM * ReachM) { (source, c) = ("camp", CampContamination(world)); }
        else if (world.Map is { } map && Nearby(map.Grid, t.X, t.Z) is { } kind) { (source, c) = (kind, SourceContamination(kind)); }
        else if (world.Map is { } m2 && Seaside(m2.Grid, t.X, t.Z)) { (source, c) = ("sea", 0f); }
        else { return "there's no water here"; }

        var id = world.People.Ids[row];
        if (source == "sea")   // 11 §11.1: brackish or sea water — +5 now, −15 over the hour (applied at once: net −10)
        {
            n.Hydration = Math.Clamp(n.Hydration - 10f, 0f, 100f);
            world.Emit(Salience.Minor, id, new Drank(id, -10f, "sea"));
            return null;
        }

        var gain = MathF.Min(DrinkHydration, 100f - n.Hydration);
        if (gain <= 0f) { return "you aren't thirsty"; }
        n.Hydration += gain;
        Exposure(world, row, gain, c, boiled: false, now);   // a drink from the bank isn't boiled
        world.Emit(Salience.Minor, id, new Drank(id, gain, source));
        return null;
    }

    private static string? Nearby(WorldGrid g, float x, float z)
    {
        for (var a = 0; a < 8; a++)
        {
            var (ox, oz) = (MathF.Cos(a * MathF.PI / 4f) * 3f, MathF.Sin(a * MathF.PI / 4f) * 3f);
            if (KindAt(g, x + ox, z + oz) is { } k) { return k; }
        }

        return KindAt(g, x, z);
    }

    private static bool Seaside(WorldGrid g, float x, float z)
    {
        var half = (g.Size - 1) * g.CellM / 2f;
        for (var a = 0; a < 8; a++)
        {
            float px = x + (MathF.Cos(a * MathF.PI / 4f) * 4f), pz = z + (MathF.Sin(a * MathF.PI / 4f) * 4f);
            int c = (int)MathF.Round((px + half) / g.CellM), r = (int)MathF.Round((pz + half) / g.CellM);
            if (c >= 0 && r >= 0 && c < g.Size && r < g.Size && g.Land[(r * g.Size) + c] == 0) { return true; }
        }

        return false;
    }

    /// <summary>11 §7.4 background Flux per person per day: 0.002 × (1 + (60 − Sanitation)/30) when Sanitation &lt; 60.</summary>
    public static float BackgroundFluxPerDay(int sanitation) => sanitation >= 60 ? 0f : 0.002f * (1f + ((60f - sanitation) / 30f));
}
