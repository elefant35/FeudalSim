using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>
/// 10 §3.2 stage 3: deterministic hydraulic erosion (droplets carrying water and sediment, with inertia; erosion through
/// a small brush, deposition as they slow) and thermal relaxation at the talus angle, on the 8 m grid. Ordered by nature —
/// droplets run one after another from keyed start points — so it is serial and identical on every run.
/// </summary>
public static class Erosion
{
    public const int Droplets = 60_000, Lifetime = 40, BrushRadius = 2;
    private const float Inertia = 0.05f, Capacity = 4f, MinSlope = 0.01f, ErodeSpeed = 0.3f, DepositSpeed = 0.3f, Evaporate = 0.02f, Gravity = 4f;

    public static void Hydraulic(WorldGrid g, ulong key, int droplets = Droplets)
    {
        var n = g.Size;
        var h = g.Height;
        var (offsets, weights) = Brush(n);
        var rng = new Rng(key);
        for (var d = 0; d < droplets; d++)
        {
            float x = rng.Uniform(1, n - 2), z = rng.Uniform(1, n - 2);
            var start = ((int)z * n) + (int)x;
            if (g.Land[start] == 0) { continue; }   // rain on land only
            float dx = 0, dz = 0, speed = 1, water = 1, sediment = 0;
            for (var life = 0; life < Lifetime; life++)
            {
                int cx = (int)x, cz = (int)z;
                var cell = (cz * n) + cx;
                float ox = x - cx, oz = z - cz;
                var (height, gx, gz) = HeightAndGradient(h, n, x, z);
                dx = (dx * Inertia) - (gx * (1 - Inertia));
                dz = (dz * Inertia) - (gz * (1 - Inertia));
                var len = MathF.Sqrt((dx * dx) + (dz * dz));
                if (len < 1e-6f) { break; }
                dx /= len; dz /= len;
                x += dx; z += dz;
                if (x < 1 || z < 1 || x >= n - 2 || z >= n - 2) { break; }
                var newHeight = HeightAndGradient(h, n, x, z).H;
                var dh = newHeight - height;
                if (newHeight < 0f && dh < 0) { break; }   // reached the sea
                var capacity = MathF.Max(-dh, MinSlope) * speed * water * Capacity;
                if (sediment > capacity || dh > 0)
                {
                    // Deposit (fill the pit uphill, or drop the excess) on the four cells around the old position.
                    var amount = dh > 0 ? MathF.Min(dh, sediment) : (sediment - capacity) * DepositSpeed;
                    sediment -= amount;
                    h[cell] += amount * (1 - ox) * (1 - oz);
                    h[cell + 1] += amount * ox * (1 - oz);
                    h[cell + n] += amount * (1 - ox) * oz;
                    h[cell + n + 1] += amount * ox * oz;
                }
                else
                {
                    var amount = MathF.Min((capacity - sediment) * ErodeSpeed, -dh);
                    if (cz > BrushRadius && cx > BrushRadius && cz < n - BrushRadius - 1 && cx < n - BrushRadius - 1)
                    {
                        for (var k = 0; k < offsets.Length; k++)
                        {
                            var idx = cell + offsets[k];
                            var take = MathF.Min(h[idx] + 40f, amount * weights[k]);   // never dig below the sea floor
                            h[idx] -= take;
                            sediment += take;
                        }
                    }
                }

                speed = MathF.Sqrt(MathF.Max(0f, (speed * speed) + (dh * Gravity)));
                water *= 1 - Evaporate;
            }
        }
    }

    /// <summary>Thermal relaxation: material slides off slopes steeper than the talus angle (35°).</summary>
    public static void Thermal(WorldGrid g, int passes = 2)
    {
        var n = g.Size;
        var h = g.Height;
        var talus = g.CellM * MathF.Tan(35f * MathF.PI / 180f);
        for (var p = 0; p < passes; p++)
        {
            for (var r = 1; r < n - 1; r++)
            {
                for (var c = 1; c < n - 1; c++)
                {
                    var i = (r * n) + c;
                    Slide(h, i, i - 1, talus);
                    Slide(h, i, i + 1, talus);
                    Slide(h, i, i - n, talus);
                    Slide(h, i, i + n, talus);
                }
            }
        }
    }

    private static void Slide(float[] h, int i, int j, float talus)
    {
        var diff = h[i] - h[j];
        if (diff > talus) { var move = (diff - talus) * 0.25f; h[i] -= move; h[j] += move; }
    }

    private static (float H, float Gx, float Gz) HeightAndGradient(float[] h, int n, float x, float z)
    {
        int cx = (int)x, cz = (int)z;
        float ox = x - cx, oz = z - cz;
        var i = (cz * n) + cx;
        float nw = h[i], ne = h[i + 1], sw = h[i + n], se = h[i + n + 1];
        var gx = ((ne - nw) * (1 - oz)) + ((se - sw) * oz);
        var gz = ((sw - nw) * (1 - ox)) + ((se - ne) * ox);
        return ((nw * (1 - ox) * (1 - oz)) + (ne * ox * (1 - oz)) + (sw * (1 - ox) * oz) + (se * ox * oz), gx, gz);
    }

    private static (int[] Offsets, float[] Weights) Brush(int n)
    {
        var offsets = new List<int>();
        var weights = new List<float>();
        for (var dz = -BrushRadius; dz <= BrushRadius; dz++)
        {
            for (var dx = -BrushRadius; dx <= BrushRadius; dx++)
            {
                var d = MathF.Sqrt((dx * dx) + (dz * dz));
                if (d > BrushRadius) { continue; }
                offsets.Add((dz * n) + dx);
                weights.Add(1 - (d / BrushRadius));
            }
        }

        var sum = weights.Sum();
        return ([.. offsets], [.. weights.Select(w => w / sum)]);
    }

    /// <summary>The 2 m heightfield (10 §3.2: "detail upsampled to 2 m"): bilinear from the 8 m grid plus fine fBm (±0.6 m) on land.</summary>
    public static float[] Upsample2m(WorldGrid g, ulong key, IJobScheduler? jobs = null)
    {
        var factor = (int)(g.CellM / 2f);
        var n2 = ((g.Size - 1) * factor) + 1;
        var out2 = new float[n2 * n2];
        (jobs ?? SerialJobScheduler.Instance).ForEachChunk(n2, (start, end) =>
        {
            for (var r = start; r < end; r++)
            {
                for (var c = 0; c < n2; c++)
                {
                    float x = c / (float)factor, z = r / (float)factor;
                    int cx = Math.Min((int)x, g.Size - 2), cz = Math.Min((int)z, g.Size - 2);
                    float ox = x - cx, oz = z - cz;
                    var i = (cz * g.Size) + cx;
                    var h = (g.Height[i] * (1 - ox) * (1 - oz)) + (g.Height[i + 1] * ox * (1 - oz)) + (g.Height[i + g.Size] * (1 - ox) * oz) + (g.Height[i + g.Size + 1] * ox * oz);
                    if (h > 0f) { h += (float)((WorldNoise.Fbm(key, c / 6.0, r / 6.0, 3) - 0.5) * 1.2); }
                    out2[(r * n2) + c] = h;
                }
            }
        });
        return out2;
    }
}
