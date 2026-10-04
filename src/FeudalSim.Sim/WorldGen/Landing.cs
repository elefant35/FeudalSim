using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

public enum PoiKind : byte { Wreck, Flotsam }

/// <summary>10 §9 point of interest with a stable id.</summary>
public sealed record Poi(int Id, PoiKind Kind, float X, float Z);

/// <summary>10 §3.9 the chosen landing: the beach, the wreck on its reef offshore, and the hard-requirement measurements.</summary>
public sealed record LandingSite(float BeachX, float BeachZ, float WreckX, float WreckZ, float ReefM, float WaterM, float FlintM, float ClayM, float BroadleafM,
    float FertileHa, float Score);

/// <summary>
/// 10 §3.2 stage 10 (part, M2-01c-i): the player's landing. Candidates are sandy, dune or shingle beach cells within
/// 1.5 km of the primary estuary mouth. Hard requirements: fresh water ≤ 400 m (a brook of ≥ 0.25 km² counts), flint ≤ 1 km (a flint bed, chalk ground or
/// a shingle shore — 10 §5.1's three flint sources), clay ≤ 1.5 km (a clay pit), broadleaf ≤ 600 m, ≥ 40 ha of fertility ≥ 0.65 within 1.5 km, and open sea 150–300 m
/// straight out for the reef. Score: the fertile area ×3 (the landing is good for food, 10 §3.9). The reef line is then
/// shoaled to −0.8 m (wadeable at low water) and the wreck sits at its end. Flotsam strands: 6–12 beach cells within 5 km,
/// downdrift (east, with the prevailing westerlies), spaced ≥ 300 m. Tin, dens and metals join the rules when those exist.
/// </summary>
public static class Landing
{
    public const float BrookKm2 = 0.25f;

    public static (LandingSite? Site, IReadOnlyList<Poi> Pois) Choose(WorldGrid g, Hydrology.Result water, Coast.Result coast, IReadOnlyList<Deposit> deposits, ulong key)
    {
        var n = g.Size;
        var primary = coast.Estuaries.FirstOrDefault(e => e.Primary);
        if (primary is null) { return (null, []); }
        float mx = g.X(primary.MouthCell % n), mz = g.Z(primary.MouthCell / n);
        // Fresh water: springs, streams, rivers above the tide, lakes — and brooks of ≥ 0.25 km² (the flare makes the lower
        // river brackish, so a landing near the mouth drinks from the brooks that reach the shore).
        var fresh = Distance(g, i => g.Land[i] == 1 && ((WaterClass)g.Water[i] is WaterClass.Spring or WaterClass.Stream or WaterClass.River or WaterClass.Lake
            || (g.Water[i] == 0 && water.AccumKm2[i] >= BrookKm2)));
        var broadleaf = Distance(g, i => (Biome)g.Biome[i] == Biome.Broadleaf);
        var chalk = Distance(g, i => g.Land[i] == 1 && ((Lithology)g.Lithology[i] == Lithology.Chalk || (ShoreKind)coast.Shore[i] == ShoreKind.Shingle));   // 10 §5.1: chalk, nodule beds, shingle
        LandingSite? best = null;
        var bestCell = -1;
        for (var i = 0; i < g.Height.Length; i += 2)
        {
            if ((ShoreKind)coast.Shore[i] is not (ShoreKind.SandyBeach or ShoreKind.Dunes or ShoreKind.Shingle)) { continue; }
            float x = g.X(i % n), z = g.Z(i / n);
            if (Dist(x, z, mx, mz) > 1500f || fresh[i] > 400f || broadleaf[i] > 600f) { continue; }
            var flint = MathF.Min(chalk[i], Nearest(deposits, DepositKind.FlintBed, x, z));
            var clay = Nearest(deposits, DepositKind.ClayPit, x, z);
            if (flint > 1000f || clay > 1500f) { continue; }
            if (Reef(g, i) is not var (reefM, wx, wz) || reefM < 150f) { continue; }
            var fertile = FertileHa(g, i % n, i / n, 1500f);
            if (fertile < 40f) { continue; }
            var score = 3f * fertile;
            if (best is null || score > best.Score) { (best, bestCell) = (new LandingSite(x, z, wx, wz, reefM, fresh[i], flint, clay, broadleaf[i], fertile, score), i); }
        }

        if (best is null) { return (null, []); }
        ShoalReef(g, bestCell, best.ReefM);
        var pois = new List<Poi> { new(1, PoiKind.Wreck, best.WreckX, best.WreckZ) };
        var rng = new Rng(key);
        var wanted = rng.Range(6, 13);
        var strands = new List<(float X, float Z)>();
        for (var i = 0; i < g.Height.Length && strands.Count < wanted; i += 3)
        {
            if ((ShoreKind)coast.Shore[i] is not (ShoreKind.SandyBeach or ShoreKind.Dunes or ShoreKind.Shingle)) { continue; }
            float x = g.X(i % n), z = g.Z(i / n);
            var d = Dist(x, z, best.BeachX, best.BeachZ);
            if (d > 5000f || d < 300f || x < best.BeachX - 500f) { continue; }   // downdrift: east of the landing
            if ((SplitMix64.Mix(key, (ulong)i, 0, 0, 0) & 3) != 0 || strands.Any(s => Dist(s.X, s.Z, x, z) < 300f)) { continue; }
            strands.Add((x, z));
        }

        pois.AddRange(strands.Select((s, k) => new Poi(k + 2, PoiKind.Flotsam, s.X, s.Z)));
        return (best, pois);
    }

    /// <summary>Open sea straight out from a beach cell: how far (≤ 300 m) and where the wreck would sit (150–300 m out).</summary>
    private static (float ReefM, float X, float Z)? Reef(WorldGrid g, int cell)
    {
        var n = g.Size;
        int r = cell / n, c = cell % n;
        var dx = (g.Land[cell + 1] == 0 ? 1 : 0) - (g.Land[cell - 1] == 0 ? 1 : 0);
        var dz = (g.Land[cell + n] == 0 ? 1 : 0) - (g.Land[cell - n] == 0 ? 1 : 0);
        if (dx == 0 && dz == 0) { return null; }
        var steps = 0;
        for (var k = 1; k <= 300 / (int)g.CellM; k++)
        {
            int rr = r + (dz * k), cc = c + (dx * k);
            if (rr < 1 || cc < 1 || rr >= n - 1 || cc >= n - 1 || g.Land[(rr * n) + cc] == 1) { break; }
            steps = k;
        }

        var len = MathF.Sqrt((dx * dx) + (dz * dz));
        var reefM = steps * g.CellM * len;
        var out_ = Math.Min(steps, (int)(220f / (g.CellM * len)));
        return (reefM, g.X(c + (dx * out_)), g.Z(r + (dz * out_)));
    }

    /// <summary>The reef or sandbar the survivors wade along (11 §15.2): sea cells on the line shoaled to −0.8 m.</summary>
    private static void ShoalReef(WorldGrid g, int cell, float reefM)
    {
        var n = g.Size;
        int r = cell / n, c = cell % n;
        var dx = (g.Land[cell + 1] == 0 ? 1 : 0) - (g.Land[cell - 1] == 0 ? 1 : 0);
        var dz = (g.Land[cell + n] == 0 ? 1 : 0) - (g.Land[cell - n] == 0 ? 1 : 0);
        var len = MathF.Sqrt((dx * dx) + (dz * dz));
        var out_ = Math.Min((int)(reefM / (g.CellM * len)), (int)(220f / (g.CellM * len)));
        for (var k = 1; k <= out_; k++)
        {
            var i = ((r + (dz * k)) * n) + c + (dx * k);
            if (g.Land[i] == 0) { g.Height[i] = MathF.Max(g.Height[i], -0.8f); }
        }
    }

    private static float FertileHa(WorldGrid g, int c0, int r0, float radiusM)
    {
        var n = g.Size;
        var rad = (int)(radiusM / g.CellM);
        var cells = 0;
        for (var dr = -rad; dr <= rad; dr += 2)
        {
            for (var dc = -rad; dc <= rad; dc += 2)
            {
                if ((dr * dr) + (dc * dc) > rad * rad) { continue; }
                int rr = r0 + dr, cc = c0 + dc;
                if (rr < 0 || cc < 0 || rr >= n || cc >= n) { continue; }
                var i = (rr * n) + cc;
                if (g.Land[i] == 1 && g.Fertility[i] >= 166) { cells += 4; }   // fertility ≥ 0.65; sampled every other cell
            }
        }

        return cells * g.CellM * g.CellM / 1e4f;
    }

    private static float Nearest(IReadOnlyList<Deposit> deposits, DepositKind kind, float x, float z)
    {
        var best = float.MaxValue;
        foreach (var d in deposits) { if (d.Kind == kind) { best = MathF.Min(best, Dist(d.X, d.Z, x, z)); } }
        return best;
    }

    private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt(((ax - bx) * (ax - bx)) + ((az - bz) * (az - bz)));

    /// <summary>Chamfer distance (m) from cells matching <paramref name="source"/>.</summary>
    internal static float[] Distance(WorldGrid g, Func<int, bool> source)
    {
        var n = g.Size;
        var d = new float[n * n];
        var big = float.MaxValue / 4;
        for (var i = 0; i < d.Length; i++) { d[i] = source(i) ? 0f : big; }
        float a = g.CellM, b = g.CellM * 1.4142135f;
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                var i = (r * n) + c;
                if (c > 0) { d[i] = MathF.Min(d[i], d[i - 1] + a); }
                if (r > 0) { d[i] = MathF.Min(d[i], d[i - n] + a); if (c > 0) { d[i] = MathF.Min(d[i], d[i - n - 1] + b); } if (c < n - 1) { d[i] = MathF.Min(d[i], d[i - n + 1] + b); } }
            }
        }

        for (var r = n - 1; r >= 0; r--)
        {
            for (var c = n - 1; c >= 0; c--)
            {
                var i = (r * n) + c;
                if (c < n - 1) { d[i] = MathF.Min(d[i], d[i + 1] + a); }
                if (r < n - 1) { d[i] = MathF.Min(d[i], d[i + n] + a); if (c < n - 1) { d[i] = MathF.Min(d[i], d[i + n + 1] + b); } if (c > 0) { d[i] = MathF.Min(d[i], d[i + n - 1] + b); } }
            }
        }

        return d;
    }
}
