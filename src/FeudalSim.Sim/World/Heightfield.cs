using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.World;

/// <summary>
/// Deterministic terrain heights (M0-12 spike; full world generation is 10-world-and-setting's, M2).
/// fBm over keyed value noise: no RNG state, same seed → same heights on every run.
/// </summary>
public static class Heightfield
{
    /// <summary>
    /// Heights in metres for a <paramref name="size"/>×<paramref name="size"/> grid, row-major (z rows, x columns). Rows are
    /// generated in fixed 64-row chunks on <paramref name="jobs"/> (M1-S4: 8 km took 15.7 s on one thread); every height is
    /// a pure function of (seed, x, z), so the result is identical for any thread count.
    /// </summary>
    public static float[] Generate(ulong seed, int size, float spacingM, float maxHeightM = 36f, float baseCellM = 160f, int octaves = 5, IJobScheduler? jobs = null)
    {
        var heights = new float[size * size];
        (jobs ?? SerialJobScheduler.Instance).ForEachChunk(size, (start, end) => Rows(heights, start, end, seed, size, spacingM, maxHeightM, baseCellM, octaves));
        return heights;
    }

    private static void Rows(float[] heights, int start, int end, ulong seed, int size, float spacingM, float maxHeightM, float baseCellM, int octaves)
    {
        for (var z = start; z < end; z++)
        {
            for (var x = 0; x < size; x++)
            {
                double px = x * spacingM, pz = z * spacingM, amp = 1, freq = 1 / (double)baseCellM, sum = 0, norm = 0;
                for (var o = 0; o < octaves; o++)
                {
                    sum += amp * ValueNoise(seed, (ulong)o, px * freq, pz * freq);
                    norm += amp;
                    amp *= 0.5;
                    freq *= 2;
                }

                var h = sum / norm;                  // 0..1
                heights[(z * size) + x] = (float)(Math.Pow(h, 1.6) * maxHeightM);   // flatter lowlands, sharper hills
            }
        }
    }

    private static double ValueNoise(ulong seed, ulong octave, double x, double z)
    {
        var x0 = Math.Floor(x);
        var z0 = Math.Floor(z);
        var tx = Smooth(x - x0);
        var tz = Smooth(z - z0);
        var a = Lattice(seed, octave, (long)x0, (long)z0);
        var b = Lattice(seed, octave, (long)x0 + 1, (long)z0);
        var c = Lattice(seed, octave, (long)x0, (long)z0 + 1);
        var d = Lattice(seed, octave, (long)x0 + 1, (long)z0 + 1);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    private static double Lattice(ulong seed, ulong octave, long x, long z)
        => (SplitMix64.Mix(seed, 0x7E44A1, octave, (ulong)x, (ulong)z) >> 11) * (1.0 / (1UL << 53));

    private static double Smooth(double t) => t * t * (3 - (2 * t));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
