using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>
/// Keyed lattice noise for world generation (10 §3.2): value noise, fBm, ridged multifractal and domain warp. No RNG
/// state — every value is a pure function of (key, x, z), so rows can be generated in parallel and stages don't share
/// streams. Doubles throughout; per-platform determinism as ADR-0002 (the grids are saved, 10 §3.2).
/// </summary>
public static class WorldNoise
{
    public static double Value(ulong key, double x, double z)
    {
        var x0 = Math.Floor(x);
        var z0 = Math.Floor(z);
        var tx = Smooth(x - x0);
        var tz = Smooth(z - z0);
        var a = Lattice(key, (long)x0, (long)z0);
        var b = Lattice(key, (long)x0 + 1, (long)z0);
        var c = Lattice(key, (long)x0, (long)z0 + 1);
        var d = Lattice(key, (long)x0 + 1, (long)z0 + 1);
        return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), tz);
    }

    /// <summary>Fractal Brownian motion in [0, 1]: octaves of value noise, halving amplitude, doubling frequency.</summary>
    public static double Fbm(ulong key, double x, double z, int octaves, double lacunarity = 2.0, double gain = 0.5)
    {
        double sum = 0, norm = 0, amp = 1, f = 1;
        for (var o = 0; o < octaves; o++)
        {
            sum += amp * Value(SplitMix64.Mix(key, 0xF8A1, (ulong)o, 0, 0), x * f, z * f);
            norm += amp;
            amp *= gain;
            f *= lacunarity;
        }

        return sum / norm;
    }

    /// <summary>Ridged multifractal in [0, 1] (Musgrave): sharp crests where the base noise crosses its midpoint.</summary>
    public static double Ridged(ulong key, double x, double z, int octaves)
    {
        double sum = 0, norm = 0, amp = 1, f = 1, weight = 1;
        for (var o = 0; o < octaves; o++)
        {
            var n = 1 - Math.Abs((2 * Value(SplitMix64.Mix(key, 0x41D6, (ulong)o, 0, 0), x * f, z * f)) - 1);
            n *= n * weight;
            weight = Math.Clamp(n * 2, 0, 1);
            sum += amp * n;
            norm += amp;
            amp *= 0.5;
            f *= 2.1;
        }

        return sum / norm;
    }

    /// <summary>Domain warp: offsets (x, z) by two fBm fields of <paramref name="strength"/> cells.</summary>
    public static (double X, double Z) Warp(ulong key, double x, double z, double strength, int octaves = 3)
        => (x + (strength * ((2 * Fbm(SplitMix64.Mix(key, 0x3A11, 1, 0, 0), x, z, octaves)) - 1)),
            z + (strength * ((2 * Fbm(SplitMix64.Mix(key, 0x3A11, 2, 0, 0), x, z, octaves)) - 1)));

    private static double Lattice(ulong key, long x, long z) => (SplitMix64.Mix(key, 0x5EED, (ulong)x, (ulong)z, 0) >> 11) * (1.0 / (1UL << 53));

    private static double Smooth(double t) => t * t * (3 - (2 * t));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);
}
