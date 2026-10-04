using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>10 §4 biomes (byte values saved; append only).</summary>
public enum Biome : byte { None, CoastDunes, Meadow, Broadleaf, Pine, Wetland, RiverValley, HillsMoor, Highland }

/// <summary>10 §3.7 soil types.</summary>
public enum Soil : byte { None, Alluvial, BrownEarth, Rendzina, PeatGley, Podzol, Sand, StonyUpland }

/// <summary>
/// 10 §3.2 stages 7–8 (M2-01b-i): climate fields (altitude band, maritime flag, wind exposure, rain shadow, frost hollows),
/// then biomes by §3.7's priority rules, two majority-filter passes and the 0.5 ha minimum patch, then soils and fertility.
/// The breadbasket is the largest connected area of fertility ≥ 0.7. Serial and integer-ordered (deterministic).
/// </summary>
public static class Biomes
{
    public const byte FrostHollow = 1, Maritime = 2;
    public const int MinPatchCells = 78;   // 0.5 ha of 8 m cells

    public static readonly string[] Keys = ["", "coast_dunes", "meadow", "broadleaf", "pine", "wetland", "river_valley", "hills_moor", "highland"];

    public sealed record Result(IReadOnlyDictionary<Biome, float> Shares, float BreadbasketHa, float BreadbasketX, float BreadbasketZ);

    public static Result Run(WorldGrid g, Hydrology.Result water, Coast.Result coast, ulong key)
    {
        var n = g.Size;
        var h = g.Height;
        var cells = h.Length;
        var slope = new float[cells];
        var northFacing = new bool[cells];
        for (var r = 1; r < n - 1; r++)
        {
            for (var c = 1; c < n - 1; c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0) { continue; }
                slope[i] = Lithologies.SlopeDeg(g, c, r, 1);
                var dz = (h[i + n] - h[i - n]) / (2f * g.CellM);   // +z is south: the slope falls toward −z (north) when dz > 0
                var dx = (h[i + 1] - h[i - 1]) / (2f * g.CellM);
                var grad = MathF.Sqrt((dx * dx) + (dz * dz));
                northFacing[i] = slope[i] >= 8f && grad > 0f && dz >= 0.7071f * grad;   // aspect within ±45° of north
                g.Slope[i] = (byte)Math.Min(90, (int)slope[i]);
            }
        }

        var riverDist = Distance(g, i => g.Water[i] == (byte)WaterClass.River);
        var lakeDist = Distance(g, i => g.Water[i] == (byte)WaterClass.Lake);

        // Stage 8 — the §3.7 priority rules.
        var treeKey = SplitMix64.Mix(key, 0x7EE, 0, 0, 0);
        var sandKey = SplitMix64.Mix(key, 0x5A4D, 0, 0, 0);
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0 || g.Water[i] is (byte)WaterClass.Lake) { continue; }
                var shore = (ShoreKind)coast.Shore[i];
                var lith = (Lithology)g.Lithology[i];
                var accum = water.AccumKm2[i];
                var flat = Lithologies.SlopeDeg(g, c, r, 2);   // the same 2-cell slope as hydrology's wetness index (M2-01a-iii)
                var twi = MathF.Log(MathF.Max(1e-6f, accum) * 1e6f / g.CellM / MathF.Max(0.001f, MathF.Tan(MathF.Max(0.05f, flat) * MathF.PI / 180f)));
                Biome b;
                if (shore != ShoreKind.SaltMarsh && g.CoastDistM[i] <= 150f && h[i] < 12f) { b = Biome.CoastDunes; }   // §3.7 order 1 (salt marsh → Wetland)
                else if (shore == ShoreKind.SaltMarsh || (flat < 2f && (twi > 12.5f || lakeDist[i] <= 60f || lith == Lithology.Peat))) { b = Biome.Wetland; }
                else if (riverDist[i] <= 250f && slope[i] < 6f && h[i] < 150f) { b = Biome.RiverValley; }
                else if (h[i] >= 450f || (slope[i] > 30f && h[i] > 300f)) { b = Biome.Highland; }
                else if (h[i] is >= 180f and < 450f || slope[i] is >= 12f and <= 30f) { b = Biome.HillsMoor; }
                else if (SandySoil(sandKey, g, c, r, lith) || (northFacing[i] && h[i] is >= 100f and <= 500f) || (g.CoastDistM[i] is > 150f and < 500f && h[i] < 25f && shore == ShoreKind.None && NearDunes(g, coast, c, r))) { b = Biome.Pine; }
                else if (h[i] < 150f && slope[i] < 12f && TreeIndex(treeKey, g, c, r, lith) < 0.35f) { b = Biome.Meadow; }
                else { b = h[i] < 300f ? Biome.Broadleaf : Biome.HillsMoor; }
                g.Biome[i] = (byte)b;
            }
        }

        // Coast & Dunes keeps its seaward rule ahead of wetland except for salt marsh (§3.7 order 1, salt marsh → Wetland).
        Smooth(g, 2);
        MergeSmallPatches(g);

        // Soils and fertility (§3.7 table), fertility interpolated inside each soil's band by low-frequency noise.
        var fertKey = SplitMix64.Mix(key, 0xFE27, 0, 0, 0);
        var land = 0;
        var counts = new int[9];
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0 || g.Biome[i] == 0) { continue; }
                land++;
                var b = (Biome)g.Biome[i];
                counts[(int)b]++;
                var lith = (Lithology)g.Lithology[i];
                var exposed = g.CoastDistM[i] < 800f || slope[i] > 15f;
                var soil = b switch
                {
                    Biome.RiverValley => Soil.Alluvial,
                    Biome.Wetland => Soil.PeatGley,
                    Biome.Pine => Soil.Podzol,
                    Biome.CoastDunes => Soil.Sand,
                    Biome.Highland => Soil.StonyUpland,
                    Biome.HillsMoor => exposed && lith == Lithology.Peat ? Soil.Podzol : Soil.StonyUpland,
                    Biome.Meadow when lith == Lithology.Chalk => Soil.Rendzina,
                    _ => Soil.BrownEarth,
                };
                var (lo, hi) = soil switch
                {
                    Soil.Alluvial => (0.80f, 1.00f), Soil.BrownEarth => (0.60f, 0.85f), Soil.Rendzina => (0.50f, 0.60f), Soil.PeatGley => (0.20f, 0.20f),
                    Soil.Podzol => (0.25f, 0.40f), Soil.Sand => (0.05f, 0.15f), _ => (0.10f, 0.25f),
                };
                var t = (float)Math.Clamp(WorldNoise.Fbm(fertKey, g.X(c) / 900.0, g.Z(r) / 900.0, 3), 0, 1);
                g.Soil[i] = (byte)soil;
                g.Fertility[i] = (byte)Math.Clamp((int)MathF.Round((lo + ((hi - lo) * t)) * 255f, MidpointRounding.AwayFromZero), 0, 255);
            }
        }

        Climate(g, slope);
        var shares = new SortedDictionary<Biome, float>();
        for (var b = 1; b < counts.Length; b++) { shares[(Biome)b] = land == 0 ? 0f : counts[b] / (float)land; }
        var (bbHa, bbX, bbZ) = Breadbasket(g);
        return new Result(shares, bbHa, bbX, bbZ);
    }

    /// <summary>§3.7 order 7's tree-establishment index: noise, lower on chalk (downland), i.e. where meadow wins.</summary>
    private static float TreeIndex(ulong key, WorldGrid g, int c, int r, Lithology lith)
        => (float)WorldNoise.Fbm(key, g.X(c) / 1400.0, g.Z(r) / 1400.0, 4) - (lith == Lithology.Chalk ? 0.25f : 0f);

    /// <summary>§3.7 order 6's "podzol or sandy soils": patchy sandy ground over sandstone (low-frequency noise), not the whole province.</summary>
    private static bool SandySoil(ulong key, WorldGrid g, int c, int r, Lithology lith)
        => lith == Lithology.Sandstone && WorldNoise.Fbm(key, g.X(c) / 1100.0, g.Z(r) / 1100.0, 3) > 0.62;

    private static bool NearDunes(WorldGrid g, Coast.Result coast, int c, int r)
    {
        var n = g.Size;
        for (var dr = -60; dr <= 60; dr += 10)
        {
            for (var dc = -60; dc <= 60; dc += 10)
            {
                int rr = r + dr, cc = c + dc;
                if (rr >= 0 && rr < n && cc >= 0 && cc < n && coast.Shore[(rr * n) + cc] == (byte)ShoreKind.Dunes) { return true; }
            }
        }

        return false;
    }

    /// <summary>Chamfer distance (m) from cells matching <paramref name="source"/>, two passes (3-4 weights × cell/3).</summary>
    private static float[] Distance(WorldGrid g, Func<int, bool> source)
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

    /// <summary>Majority filter over the 3×3 land neighbourhood (ties keep the cell's own biome).</summary>
    private static void Smooth(WorldGrid g, int passes)
    {
        var n = g.Size;
        Span<int> votes = stackalloc int[9];
        for (var p = 0; p < passes; p++)
        {
            var next = (byte[])g.Biome.Clone();
            for (var r = 1; r < n - 1; r++)
            {
                for (var c = 1; c < n - 1; c++)
                {
                    var i = (r * n) + c;
                    if (g.Biome[i] == 0) { continue; }
                    votes.Clear();
                    for (var dr = -1; dr <= 1; dr++) { for (var dc = -1; dc <= 1; dc++) { var b = g.Biome[i + (dr * n) + dc]; if (b != 0) { votes[b]++; } } }
                    var best = g.Biome[i];
                    for (var b = 1; b < 9; b++) { if (votes[b] > votes[best]) { best = (byte)b; } }
                    next[i] = best;
                }
            }

            Array.Copy(next, g.Biome, next.Length);
        }
    }

    /// <summary>Patches under 0.5 ha take the most common biome around them (iterated until none remain).</summary>
    private static void MergeSmallPatches(WorldGrid g)
    {
        var n = g.Size;
        var label = new int[n * n];
        var queue = new Queue<int>();
        Span<int> around = stackalloc int[9];
        for (var round = 0; round < 4; round++)
        {
            Array.Fill(label, 0);
            var next = 0;
            var changed = false;
            var patch = new List<int>();
            for (var start = 0; start < label.Length; start++)
            {
                if (g.Biome[start] == 0 || label[start] != 0) { continue; }
                next++;
                patch.Clear();
                label[start] = next;
                queue.Enqueue(start);
                var biome = g.Biome[start];
                while (queue.Count > 0)
                {
                    var i = queue.Dequeue();
                    patch.Add(i);
                    int r = i / n, c = i % n;
                    foreach (var j in (ReadOnlySpan<int>)[i - 1, i + 1, i - n, i + n])
                    {
                        if (j < 0 || j >= label.Length || label[j] != 0 || g.Biome[j] != biome || (j == i - 1 && c == 0) || (j == i + 1 && c == n - 1)) { continue; }
                        label[j] = next;
                        queue.Enqueue(j);
                    }
                }

                if (patch.Count >= MinPatchCells) { continue; }
                around.Clear();
                foreach (var i in patch)
                {
                    foreach (var j in (ReadOnlySpan<int>)[i - 1, i + 1, i - n, i + n])
                    {
                        if (j >= 0 && j < label.Length && g.Biome[j] != 0 && g.Biome[j] != biome) { around[g.Biome[j]]++; }
                    }
                }

                var best = 0;
                for (var b = 1; b < 9; b++) { if (around[b] > around[best]) { best = b; } }
                if (best == 0) { continue; }   // an island patch with no other biome around: keep it
                foreach (var i in patch) { g.Biome[i] = (byte)best; }
                changed = true;
            }

            if (!changed) { break; }
        }
    }

    /// <summary>Stage 7 static climate fields (§3.7): maritime ≤ 500 m, wind exposure 0.4 (forest) … 1.3 (ridge, coast),
    /// rain shadow 0.8–1.4 from the west/south-west, frost hollows on valley floors.</summary>
    private static void Climate(WorldGrid g, float[] slope)
    {
        var n = g.Size;
        var h = g.Height;
        const int reach = 25;   // 200 m
        for (var r = reach; r < n - reach; r++)
        {
            for (var c = reach; c < n - reach; c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0) { continue; }
                var mean = (h[i - (reach * n)] + h[i + (reach * n)] + h[i - reach] + h[i + reach]) / 4f;
                var relief = h[i] - mean;   // ridge > 0, hollow < 0
                var forest = (Biome)g.Biome[i] is Biome.Broadleaf or Biome.Pine;
                var exposure = Math.Clamp(0.8f + (relief / 80f) + (g.CoastDistM[i] < 300f ? 0.3f : 0f), 0.4f, 1.3f) * (forest ? 0.5f : 1f);
                g.Exposure[i] = (byte)Math.Clamp((int)MathF.Round(MathF.Max(0.4f, exposure) * 100f), 40, 130);
                var upwind = h[i - reach - (reach / 2 * n)];   // west / south-west
                g.RainShadow[i] = (byte)Math.Clamp((int)MathF.Round(Math.Clamp(1f + ((h[i] - upwind) / 500f), 0.8f, 1.4f) * 100f), 80, 140);
                var flags = (byte)0;
                if (g.CoastDistM[i] <= 500f) { flags |= Maritime; }
                if (relief < -5f && slope[i] < 3f) { flags |= FrostHollow; }
                g.ClimateFlags[i] = flags;
            }
        }
    }

    /// <summary>The largest 8-connected area of fertility ≥ 0.7 (≥ 179/255): its size and centroid.</summary>
    private static (float Ha, float X, float Z) Breadbasket(WorldGrid g)
    {
        var n = g.Size;
        var seen = new bool[n * n];
        var queue = new Queue<int>();
        var (bestCount, bestX, bestZ) = (0, 0.0, 0.0);
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start] || g.Fertility[start] < 179) { continue; }
            seen[start] = true;
            queue.Enqueue(start);
            var (count, sx, sz) = (0, 0.0, 0.0);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                int r = i / n, c = i % n;
                count++;
                sx += g.X(c);
                sz += g.Z(r);
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int rr = r + dr, cc = c + dc;
                        if (rr < 0 || rr >= n || cc < 0 || cc >= n) { continue; }
                        var j = (rr * n) + cc;
                        if (!seen[j] && g.Fertility[j] >= 179) { seen[j] = true; queue.Enqueue(j); }
                    }
                }
            }

            if (count > bestCount) { (bestCount, bestX, bestZ) = (count, sx / count, sz / count); }
        }

        return (bestCount * g.CellM * g.CellM / 1e4f, (float)bestX, (float)bestZ);
    }
}
