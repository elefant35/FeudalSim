using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>10 §3.4 provinces. Byte values are saved; append only.</summary>
public enum Lithology : byte { Sea, Granite, Metamorphic, Sandstone, Chalk, Alluvium, Peat }

/// <summary>
/// 10 §3.2 stage 4: warped Voronoi provinces conditioned on elevation and slope (10 §3.4) — granite in the highland core,
/// a metamorphic margin around it, sandstone and shale hills, one chalk/limestone band at the coast (the flint source),
/// alluvium in flat lowlands, peat on flat wet moorland. Province labels are decided per site, so whole provinces share a rock.
/// </summary>
public static class Lithologies
{
    public const int Sites = 420;

    public static void Assign(WorldGrid g, ulong key, IJobScheduler? jobs = null)
    {
        var n = g.Size;
        var rng = new Rng(key);
        var sx = new float[Sites];
        var sz = new float[Sites];
        for (var k = 0; k < Sites; k++) { (sx[k], sz[k]) = (rng.Uniform(0, n - 1), rng.Uniform(0, n - 1)); }

        // Label each site from the terrain under it.
        var label = new Lithology[Sites];
        var peak = g.Height.Max();
        var chalkAngle = rng.Uniform(0, MathF.Tau);   // the chalk band faces one way along the coast
        for (var k = 0; k < Sites; k++)
        {
            var i = ((int)sz[k] * n) + (int)sx[k];
            var h = g.Height[i];
            var slope = SlopeDeg(g, (int)sx[k], (int)sz[k], 8);   // over ±64 m: the province's lie of the land, not one cell's roughness
            var facing = MathF.Cos(MathF.Atan2(sz[k] - (n / 2f), sx[k] - (n / 2f)) - chalkAngle);
            label[k] = h <= 0 ? Lithology.Sea
                : h > 0.62f * peak ? Lithology.Granite
                : h < 40f && g.CoastDistM[i] < 1600f && facing > 0.55f ? Lithology.Chalk
                : h < 30f && slope < 2.5f ? Lithology.Alluvium
                : (h is > 120f and < 650f && slope < 8f && rng.Chance(0.45f)) || (h < 80f && slope < 2f && g.CoastDistM[i] > 1200f && rng.Chance(0.5f)) ? Lithology.Peat   // wet moor; lowland bog (bog iron)
                : Lithology.Sandstone;
        }

        // The metamorphic margin: non-granite sites close to granite ones.
        for (var k = 0; k < Sites; k++)
        {
            if (label[k] is Lithology.Granite or Lithology.Sea) { continue; }
            for (var m = 0; m < Sites; m++)
            {
                if (label[m] != Lithology.Granite) { continue; }
                var d = MathF.Sqrt(((sx[k] - sx[m]) * (sx[k] - sx[m])) + ((sz[k] - sz[m]) * (sz[k] - sz[m]))) * g.CellM;
                if (d < 900f) { label[k] = Lithology.Metamorphic; break; }
            }
        }

        // Nearest (warped) site per cell, through a coarse bucket grid.
        const int bucketCells = 64;
        var nb = (n + bucketCells - 1) / bucketCells;
        var buckets = new List<int>[nb * nb];
        for (var b = 0; b < buckets.Length; b++) { buckets[b] = []; }
        for (var k = 0; k < Sites; k++) { buckets[((int)(sz[k] / bucketCells) * nb) + (int)(sx[k] / bucketCells)].Add(k); }
        var warpKey = SplitMix64.Mix(key, 0x1170, 0, 0, 0);
        (jobs ?? SerialJobScheduler.Instance).ForEachChunk(n, (start, end) =>
        {
            for (var r = start; r < end; r++)
            {
                for (var c = 0; c < n; c++)
                {
                    var i = (r * n) + c;
                    if (g.Height[i] <= 0) { g.Lithology[i] = (byte)Lithology.Sea; continue; }
                    var (wx, wz) = WorldNoise.Warp(warpKey, c / 90.0, r / 90.0, 0.6);
                    float px = (float)(wx * 90), pz = (float)(wz * 90);
                    int bx = Math.Clamp((int)(px / bucketCells), 0, nb - 1), bz = Math.Clamp((int)(pz / bucketCells), 0, nb - 1);
                    var best = -1;
                    var bestD = float.MaxValue;
                    for (var ring = 1; best < 0 || ring <= 2; ring++)
                    {
                        for (var zz = Math.Max(0, bz - ring); zz <= Math.Min(nb - 1, bz + ring); zz++)
                        {
                            for (var xx = Math.Max(0, bx - ring); xx <= Math.Min(nb - 1, bx + ring); xx++)
                            {
                                foreach (var k in buckets[(zz * nb) + xx])
                                {
                                    var d = ((sx[k] - px) * (sx[k] - px)) + ((sz[k] - pz) * (sz[k] - pz));
                                    if (d < bestD || (d == bestD && k < best)) { (bestD, best) = (d, k); }
                                }
                            }
                        }

                        if (ring > nb) { break; }
                    }

                    var lith = label[best];
                    g.Lithology[i] = (byte)(lith == Lithology.Sea ? Lithology.Alluvium : lith);   // shore cells under a sea site: alluvium
                }
            }
        });
    }

    public static float SlopeDeg(WorldGrid g, int c, int r, int radius = 1)
    {
        var n = g.Size;
        c = Math.Clamp(c, radius, n - 1 - radius);
        r = Math.Clamp(r, radius, n - 1 - radius);
        var i = (r * n) + c;
        var gx = (g.Height[i + radius] - g.Height[i - radius]) / (2 * radius * g.CellM);
        var gz = (g.Height[i + (radius * n)] - g.Height[i - (radius * n)]) / (2 * radius * g.CellM);
        return MathF.Atan(MathF.Sqrt((gx * gx) + (gz * gz))) * 180f / MathF.PI;
    }
}
