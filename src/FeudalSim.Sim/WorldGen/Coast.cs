using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>10 §3.6 shore kinds (saved; append only).</summary>
public enum ShoreKind : byte { None, SandyBeach, Dunes, Shingle, Rocky, Cliff, Mudflat, SaltMarsh }

public sealed record Estuary(int MouthCell, float MouthWidthM, float TidalReachM, int TidalLimitCell, float CatchmentKm2, bool Primary);

/// <summary>
/// 10 §3.2 stage 6 (10 §3.6): the primary estuary on the river with the largest catchment (a second with the spec's chance),
/// flared over its tidal reach to the drawn mouth width with mudflats and salt marsh; shores classified — dunes on the
/// coast facing the westerlies, shingle under chalk, rocky under granite and slate, cliffs where the slope passes 45°.
/// </summary>
public static class Coast
{
    public sealed record Result(IReadOnlyList<Estuary> Estuaries, byte[] Shore, float CliffShare);

    public static Result Run(WorldGrid g, Hydrology.Result water, Content.WorldSpecDef spec, ulong key)
    {
        var n = g.Size;
        var rng = new Rng(key);
        var estuaries = new List<Estuary>();
        var rivers = water.Rivers.OrderByDescending(r => r.CatchmentKm2).ThenBy(r => r.OutletCell).ToList();
        var est = spec.Estuary;
        for (var k = 0; k < rivers.Count && k < 2; k++)
        {
            if (k == 1 && !(est is not null && rng.Chance(est.SecondaryChance))) { break; }
            var river = rivers[k];
            var mouth = est?.MouthWidthM is [var m0, var m1] ? rng.Uniform(m0, m1) : 400f;
            var reach = est?.TidalReachM is [var r0, var r1] ? rng.Uniform(r0, r1) : 2000f;
            if (k == 1) { (mouth, reach) = (mouth * 0.5f, reach * 0.6f); }
            var limit = Flare(g, water, river.OutletCell, mouth, reach);
            estuaries.Add(new Estuary(river.OutletCell, mouth, reach, limit, river.CatchmentKm2, k == 0));
        }

        // Shores.
        var shore = new byte[n * n];
        int coastCells = 0, cliffs = 0;
        var west = new Vector(-1, 0);   // prevailing westerlies (10 §6.3): sandy shores facing west get dunes
        for (var r = 1; r < n - 1; r++)
        {
            for (var c = 1; c < n - 1; c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0) { continue; }
                var seaward = new Vector((g.Land[i + 1] == 0 ? 1 : 0) - (g.Land[i - 1] == 0 ? 1 : 0), (g.Land[i + n] == 0 ? 1 : 0) - (g.Land[i - n] == 0 ? 1 : 0));
                if (seaward.X == 0 && seaward.Z == 0) { continue; }
                coastCells++;
                if (g.Water[i] == (byte)WaterClass.Brackish) { continue; }
                var slope = Lithologies.SlopeDeg(g, c, r);
                var lith = (Lithology)g.Lithology[i];
                ShoreKind kind;
                if (slope > 45f) { kind = ShoreKind.Cliff; cliffs++; }
                else if (lith == Lithology.Chalk) { kind = ShoreKind.Shingle; }
                else if (lith is Lithology.Granite or Lithology.Metamorphic) { kind = ShoreKind.Rocky; }
                else { kind = (seaward.X * west.X) + (seaward.Z * west.Z) > 0 ? ShoreKind.Dunes : ShoreKind.SandyBeach; }
                shore[i] = (byte)kind;
            }
        }

        return new Result(estuaries, shore, coastCells == 0 ? 0 : cliffs / (float)coastCells);
    }

    /// <summary>
    /// Widens the last <paramref name="reachM"/> of a river toward the sea to <paramref name="mouthM"/> wide (linear flare),
    /// lowering it below sea level as brackish water, with mudflats and marsh at its edges. Returns the tidal-limit cell.
    /// </summary>
    private static int Flare(WorldGrid g, Hydrology.Result water, int outlet, float mouthM, float reachM)
    {
        var n = g.Size;
        // Walk upstream from the outlet along the largest tributary.
        var path = new List<int> { outlet };
        var current = outlet;
        while (path.Count * g.CellM < reachM)
        {
            var best = -1;
            var bestA = 0f;
            int r = current / n, c = current % n;
            for (var dr = -1; dr <= 1; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    int rr = r + dr, cc = c + dc;
                    if ((dr == 0 && dc == 0) || rr < 0 || cc < 0 || rr >= n || cc >= n) { continue; }
                    var j = (rr * n) + cc;
                    if (water.Flow[j] < 0 || g.Land[j] == 0) { continue; }
                    var down = ((rr + Dr(water.Flow[j])) * n) + cc + Dc(water.Flow[j]);
                    if (down == current && water.AccumKm2[j] > bestA) { (bestA, best) = (water.AccumKm2[j], j); }
                }
            }

            if (best < 0) { break; }
            path.Add(best);
            current = best;
        }

        for (var p = 0; p < path.Count; p++)
        {
            var t = 1f - (p / (float)Math.Max(1, path.Count - 1));   // 1 at the mouth
            var halfWidth = (mouthM * t * t / 2f) + 4f;
            var radius = (int)(halfWidth / g.CellM) + 1;
            int r = path[p] / n, c = path[p] % n;
            for (var dr = -radius; dr <= radius; dr++)
            {
                for (var dc = -radius; dc <= radius; dc++)
                {
                    int rr = r + dr, cc = c + dc;
                    if (rr < 0 || cc < 0 || rr >= n || cc >= n) { continue; }
                    var j = (rr * n) + cc;
                    var d = MathF.Sqrt((dr * dr) + (dc * dc)) * g.CellM;
                    if (d > halfWidth + (2 * g.CellM)) { continue; }
                    if (d <= halfWidth)
                    {
                        if (g.Height[j] > -1f) { g.Height[j] = -0.5f - (1.5f * t); }
                        g.Water[j] = (byte)WaterClass.Brackish;
                    }
                    else if (g.Land[j] == 1 && g.Height[j] < 3f) { g.Water[j] = (byte)WaterClass.MarshPool; }   // mudflat / salt-marsh fringe
                }
            }
        }

        return path[^1];
    }

    private static int Dr(sbyte d) => d switch { 1 or 2 or 3 => 1, 5 or 6 or 7 => -1, _ => 0 };

    private static int Dc(sbyte d) => d switch { 0 or 1 or 7 => 1, 3 or 4 or 5 => -1, _ => 0 };

    private readonly record struct Vector(int X, int Z);
}

/// <summary>10 §3.6 tides: semidiurnal, 12.42 h, range 3.2 m at springs and 1.6 m at neaps following the moon; low water ≈ 09:30 on Y0 Spring 1.</summary>
public static class Tides
{
    public const double PeriodH = 12.42, SpringRangeM = 3.2, NeapRangeM = 1.6, LunarDays = 29.53;

    /// <summary>Sea level (m) relative to mean at a game minute since Y0 Spring 1 00:00.</summary>
    public static double Level(long gameMinute)
    {
        var hours = gameMinute / 60.0;
        var range = NeapRangeM + ((SpringRangeM - NeapRangeM) * 0.5 * (1 + Math.Cos(2 * Math.PI * hours / 24 / LunarDays)));   // springs at the new moon (day 0)
        const double lowWaterH = 9.5;   // the Landfall salvage window (11)
        return -0.5 * range * Math.Cos(2 * Math.PI * (hours - lowWaterH) / PeriodH);
    }
}
