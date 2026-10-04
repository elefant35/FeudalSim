namespace FeudalSim.Sim.Core;

/// <summary>Random streams. Append only — values feed every keyed draw (20 §8.2).</summary>
public enum RngStream : ushort
{
    WorldGen = 1, Needs, Ai, Social, Rumor, Combat, Crafting, Farming, Weather, Economy, Law,
    Health, Births, Lod, Battle, Scenario, PersonGen,
}

/// <summary>Call-site salts. Every constant must be unique (enforced by a unit test).</summary>
public static class Salt
{
    public const uint WanderTarget = 1;
    public const uint WanderPause = 2;
    public const uint DecisionPolicy = 3;
    public const uint ScenarioSpawn = 4;
    public const uint DecisionPolicyResample = 5;
    public const uint PersonGen = 6;
    public const uint AiDecide = 7;
    public const uint Shipmates = 8;
    public const uint Perception = 9;
    public const uint ChatPartner = 10;
    public const uint Interaction = 11;
    public const uint InteractionLod2 = 12;
    public const uint Lod3 = 13;
    public const uint Initiative = 14;
    public const uint Brawl = 15;
    public const uint LieTest = 16;
    public const uint PersonName = 17;
    public const uint TheftDetect = 18;
    public const uint WorldGenLandmass = 19;
    public const uint WorldGenRelief = 20;
    public const uint WorldGenErosion = 21;
    public const uint WorldGenLithology = 22;
    public const uint WorldGenHydrology = 23;
    public const uint WorldGenCoast = 24;
    public const uint WorldGenBiomes = 32;
    public const uint WorldGenNodes = 33;
    public const uint WorldGenDeposits = 34;
    public const uint WorldGenLanding = 35;
    public const uint Mediation = 25;
    public const uint Trauma = 26;
    public const uint Infection = 27;
    public const uint Treatment = 28;
    public const uint Appraisal = 29;
    public const uint CraftStage = 30;
    public const uint Minigame = 31;
    public const uint WorkAccident = 36;
    public const uint ForageId = 37;
    public const uint Condition = 38;
    public const uint Contagion = 39;
    public const uint WaterExposure = 40;
    public const uint Eat = 41;
    public const uint Fall = 42;
}

/// <summary>SplitMix64 finalizer-based mixing (Steele, Lea &amp; Flood 2014).</summary>
public static class SplitMix64
{
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    public static ulong Avalanche(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Mix(ulong a, ulong b, ulong c, ulong d, ulong e)
    {
        var h = Avalanche(a + Golden);
        h = Avalanche(h ^ (b + Golden));
        h = Avalanche(h ^ (c + Golden));
        h = Avalanche(h ^ (d + Golden));
        return Avalanche(h ^ (e + Golden));
    }
}

/// <summary>
/// Keyed randomness: every draw is a pure function of (world seed, stream, subject, step, salt).
/// No RNG state exists in a save except the world seed.
/// </summary>
public static class SimRandom
{
    public static Rng For(ulong worldSeed, long step, RngStream stream, EntityId subject, uint salt)
        => new(SplitMix64.Mix(worldSeed, (ulong)stream, subject.Value, (ulong)step, salt));

    public static Rng For(in StepContext ctx, RngStream stream, EntityId subject, uint salt)
        => For(ctx.WorldSeed, ctx.Step, stream, subject, salt);
}

/// <summary>xoshiro128** seeded from a 64-bit key. Value type; no allocation.</summary>
public struct Rng
{
    private uint _s0, _s1, _s2, _s3;

    public Rng(ulong key)
    {
        var a = SplitMix64.Avalanche(key + 0x9E3779B97F4A7C15UL);
        var b = SplitMix64.Avalanche(a + 0x9E3779B97F4A7C15UL);
        _s0 = (uint)a; _s1 = (uint)(a >> 32); _s2 = (uint)b; _s3 = (uint)(b >> 32);
        if ((_s0 | _s1 | _s2 | _s3) == 0) { _s0 = 1; }
    }

    public uint NextUInt()
    {
        var result = RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 9;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3; _s2 ^= t;
        _s3 = RotateLeft(_s3, 11);
        return result;
    }

    /// <summary>Uniform in [0, 1) with 24 bits of precision.</summary>
    public float NextFloat01() => (NextUInt() >> 8) * (1.0f / 16_777_216f);

    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) { throw new ArgumentOutOfRangeException(nameof(maxExclusive)); }
        var span = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)((ulong)NextUInt() * span >> 32);
    }

    public bool Chance(float p) => NextFloat01() < p;

    /// <summary>Uniform in [min, max).</summary>
    public float Uniform(float min, float max) => min + ((max - min) * NextFloat01());

    /// <summary>Standard normal N(0, 1) by Box–Muller (one value per call; transcendental math via <see cref="SimMath"/>).</summary>
    public float NextNormal()
    {
        var u1 = 1f - NextFloat01();   // (0, 1]: avoids log(0)
        var u2 = NextFloat01();
        return MathF.Sqrt(-2f * SimMath.Log(u1)) * SimMath.Cos(2f * MathF.PI * u2);
    }

    public float Normal(float mean, float sd) => mean + (sd * NextNormal());

    private static uint RotateLeft(uint x, int k) => (x << k) | (x >> (32 - k));
}
