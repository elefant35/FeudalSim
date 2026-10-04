using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

public enum ReliefArchetype : byte { Spine, Massif, TwinRidges }

/// <summary>The 8 m world grid (10 §3.2): (Size × Size) samples covering <c>RegionM</c>, centred on the origin, row-major (z rows).</summary>
public sealed class WorldGrid(int size, float cellM)
{
    public int Size { get; } = size;
    public float CellM { get; } = cellM;
    public float[] Height { get; } = new float[size * size];

    /// <summary>1 = land (above sea level after stage 1).</summary>
    public byte[] Land { get; } = new byte[size * size];

    /// <summary>Distance to the shoreline in metres (land and sea), from stage 2.</summary>
    public float[] CoastDistM { get; } = new float[size * size];

    /// <summary>Stage 4 province per cell (<see cref="WorldGen.Lithology"/>).</summary>
    public byte[] Lithology { get; } = new byte[size * size];

    public float X(int col) => (col * CellM) - ((Size - 1) * CellM / 2f);

    public float Z(int row) => (row * CellM) - ((Size - 1) * CellM / 2f);
}

/// <summary>A generated world (10 §3.12 sketch; grids and features grow stage by stage).</summary>
public sealed record WorldGenResult(ulong Seed, int Attempt, ulong AttemptSeed, string SpecId, ReliefArchetype Archetype, float RotationRad, bool Mirrored,
    WorldGrid Grid, float LandAreaKm2, float PeakM, float AreaAbove800Km2, int Islets, IReadOnlyList<string> Failures)
{
    public bool Valid => Failures.Count == 0;
}

/// <summary>
/// 10 §3 world generation, M2-01a part (i): stage 1 landmass and stage 2 relief on the 8 m grid, validated against W1
/// (land area, islets) and W5 (peak) with retries on a derived seed (§3.11). Pure: the spec comes compiled, the result is
/// arrays. Per-cell stages run as row chunks on the job scheduler; anything ordered (bisection, distance transform,
/// components) is serial.
/// </summary>
public static class WorldGenerator
{
    public const float CellM = 8f;

    public static WorldGenResult Generate(WorldSpecDef spec, ulong seed, IJobScheduler? jobs = null)
    {
        jobs ??= SerialJobScheduler.Instance;
        WorldGenResult? last = null;
        for (var attempt = 0; attempt < Math.Max(1, spec.MaxAttempts); attempt++)
        {
            var s = attempt == 0 ? seed : SplitMix64.Mix(seed, 0x2E7A1, (ulong)attempt, 0, 0);   // §3.11: seed' = Hash(seed, attempt)
            last = Attempt(spec, seed, attempt, s, jobs);
            if (last.Valid) { return last; }
        }

        return last!;
    }

    public static WorldGenResult Attempt(WorldSpecDef spec, ulong seed, int attempt, ulong s, IJobScheduler jobs)
    {
        var size = (int)(spec.RegionM / CellM) + 1;
        var grid = new WorldGrid(size, CellM);

        // Stage 1 — landmass: archetype (weighted), rotation and mirror; domain-warped fBm under an archetype falloff.
        var rng = new Rng(SplitMix64.Mix(s, (ulong)RngStream.WorldGen, Salt.WorldGenLandmass, 0, 0));
        var archetype = PickArchetype(spec, rng.NextFloat01());
        var rotation = rng.Uniform(0f, MathF.Tau);
        var mirrored = rng.Chance(0.5f);
        var targetKm2 = rng.Uniform(spec.LandAreaKm2[0], spec.LandAreaKm2[1]);
        var landKey = SplitMix64.Mix(s, (ulong)RngStream.WorldGen, Salt.WorldGenLandmass, 1, 0);
        var mask = new float[size * size];
        jobs.ForEachChunk(size, (start, end) => MaskRows(mask, start, end, size, landKey, archetype, rotation, mirrored));
        var sea = SeaLevel(mask, targetKm2 * 1e6 / (CellM * CellM));
        for (var i = 0; i < mask.Length; i++) { grid.Land[i] = mask[i] > sea ? (byte)1 : (byte)0; }

        // Stage 2 — relief: distance to the shore, then archetype ridges + hills + coastal plain, peak normalised into its band.
        DistanceTransform(grid);
        var peakTarget = new Rng(SplitMix64.Mix(s, (ulong)RngStream.WorldGen, Salt.WorldGenRelief, 0, 0)).Uniform(spec.PeakM[0], spec.PeakM[1]);
        var reliefKey = SplitMix64.Mix(s, (ulong)RngStream.WorldGen, Salt.WorldGenRelief, 1, 0);
        var mountain = new float[size * size];
        jobs.ForEachChunk(size, (start, end) => ReliefRows(grid, mountain, start, end, reliefKey, archetype, rotation, mirrored));
        var maxMountain = 1e-6f;
        for (var i = 0; i < mountain.Length; i++) { if (grid.Land[i] == 1 && mountain[i] > maxMountain) { maxMountain = mountain[i]; } }
        var scale = peakTarget / maxMountain;
        jobs.ForEachChunk(size, (start, end) =>
        {
            for (var i = start * size; i < end * size; i++)
            {
                grid.Height[i] = grid.Land[i] == 1 ? MathF.Max(0.5f, (mountain[i] * scale) + grid.Height[i]) : -MathF.Min(40f, 2f + (grid.CoastDistM[i] * 0.02f));
            }
        });

        // The plain and hills sit on top of the mountains: rescale land heights so the peak is the drawn target exactly.
        var top = 0f;
        for (var i = 0; i < grid.Height.Length; i++) { if (grid.Land[i] == 1 && grid.Height[i] > top) { top = grid.Height[i]; } }
        if (top > 0f)
        {
            var k = peakTarget / top;
            for (var i = 0; i < grid.Height.Length; i++) { if (grid.Land[i] == 1) { grid.Height[i] = MathF.Max(0.5f, grid.Height[i] * k); } }
        }

        // Stage 3 — erosion (serial: droplets are ordered), then stage 4 — lithology.
        Erosion.Hydraulic(grid, SplitMix64.Mix(s, (ulong)RngStream.WorldGen, Salt.WorldGenErosion, 0, 0));
        Erosion.Thermal(grid);
        var eroded = 0f;
        for (var i = 0; i < grid.Height.Length; i++) { if (grid.Land[i] == 1 && grid.Height[i] > eroded) { eroded = grid.Height[i]; } }
        var renorm = eroded > 0f ? peakTarget / eroded : 1f;   // erosion wears the peak down: keep the drawn target
        for (var i = 0; i < grid.Height.Length; i++) { if (grid.Land[i] == 1) { grid.Height[i] = MathF.Max(0.5f, grid.Height[i] * renorm); } }
        Lithologies.Assign(grid, SplitMix64.Mix(s, (ulong)RngStream.WorldGen, Salt.WorldGenLithology, 0, 0), jobs);

        // Validation (10 §3.11): W1 land area and islets, W5 peak.
        var land = 0;
        var above800 = 0;
        var peak = 0f;
        for (var i = 0; i < grid.Height.Length; i++)
        {
            if (grid.Land[i] == 0) { continue; }
            land++;
            if (grid.Height[i] > 800f) { above800++; }
            peak = MathF.Max(peak, grid.Height[i]);
        }

        var cellKm2 = CellM * CellM / 1e6f;
        var islets = Islets(grid, 1e4f / (CellM * CellM));
        var failures = new List<string>();
        var landKm2 = land * cellKm2;
        if (landKm2 < spec.LandAreaKm2[0] || landKm2 > spec.LandAreaKm2[1]) { failures.Add($"W1 land {landKm2:F1} km²"); }
        if (islets is < 3 or > 10) { failures.Add($"W1 islets {islets}"); }
        if (peak < spec.PeakM[0] || peak > spec.PeakM[1]) { failures.Add($"W5 peak {peak:F0} m"); }
        if (above800 * cellKm2 < 0.2f) { failures.Add($"W5 above 800 m {above800 * cellKm2:F2} km²"); }
        return new WorldGenResult(seed, attempt, s, spec.Id, archetype, rotation, mirrored, grid, landKm2, peak, above800 * cellKm2, islets, failures);
    }

    private static ReliefArchetype PickArchetype(WorldSpecDef spec, float u)
    {
        var w = spec.ReliefArchetypeWeights;
        float spine = w.GetValueOrDefault("spine"), massif = w.GetValueOrDefault("massif"), twin = w.GetValueOrDefault("twin_ridges");
        var total = spine + massif + twin;
        var x = u * (total <= 0 ? 1 : total);
        return x < spine ? ReliefArchetype.Spine : x < spine + massif ? ReliefArchetype.Massif : ReliefArchetype.TwinRidges;
    }

    /// <summary>The archetype frame: normalised (u, v) ∈ [-1, 1] mirrored and rotated.</summary>
    private static (double U, double V) Frame(double u, double v, float rotation, bool mirrored)
    {
        if (mirrored) { u = -u; }
        var (c, sn) = (Math.Cos(rotation), Math.Sin(rotation));
        return ((c * u) - (sn * v), (sn * u) + (c * v));
    }

    private static void MaskRows(float[] mask, int start, int end, int size, ulong key, ReliefArchetype archetype, float rotation, bool mirrored)
    {
        for (var row = start; row < end; row++)
        {
            for (var col = 0; col < size; col++)
            {
                var u = (2.0 * col / (size - 1)) - 1;
                var v = (2.0 * row / (size - 1)) - 1;
                var (fu, fv) = Frame(u, v, rotation, mirrored);
                var (ex, ez) = archetype == ReliefArchetype.Massif ? (1.0, 1.0) : (0.96, 1.08);   // ridged islands run long
                var r = Math.Pow(Math.Pow(Math.Abs(fu * ex), 3) + Math.Pow(Math.Abs(fv * ez), 3), 1.0 / 3);   // superellipse: 35–45 km² of a 67 km² region
                var falloff = 1 - SmoothStep(0.50, 1.00, r);
                var (wx, wz) = WorldNoise.Warp(key, u * 2.0, v * 2.0, 0.9);
                var body = WorldNoise.Fbm(key, wx, wz, 5);
                var shore = WorldNoise.Fbm(SplitMix64.Mix(key, 0x15E7, 0, 0, 0), u * 14, v * 14, 3);   // small-scale coves
                var isletField = WorldNoise.Fbm(SplitMix64.Mix(key, 0x1517, 0, 0, 0), u * 11, v * 11, 2);
                var islet = Math.Max(0, isletField - 0.70) * 3.0 * (SmoothStep(0.78, 0.9, r) - SmoothStep(0.97, 1.02, r));   // offshore islets (W1)
                var edge = Math.Max(Math.Abs(u), Math.Abs(v)) > 0.985 ? 2.0 : 0.0;   // the region border is sea
                mask[(row * size) + col] = (float)((0.55 * falloff) + (0.36 * body) + (0.15 * shore) + islet - edge);
            }
        }
    }

    /// <summary>Bisection on the threshold so the land count hits <paramref name="targetCells"/> (10 §3.2 stage 1).</summary>
    private static float SeaLevel(float[] mask, double targetCells)
    {
        float lo = -1f, hi = 2f;
        for (var it = 0; it < 40; it++)
        {
            var mid = (lo + hi) / 2f;
            var count = 0;
            foreach (var m in mask) { if (m > mid) { count++; } }
            if (count > targetCells) { lo = mid; } else { hi = mid; }
        }

        return (lo + hi) / 2f;
    }

    /// <summary>Two-pass chamfer distance to the shoreline (land and sea sides), in metres.</summary>
    private static void DistanceTransform(WorldGrid g)
    {
        var n = g.Size;
        var d = g.CoastDistM;
        const float big = 1e9f, a = CellM, b = CellM * 1.41421356f;
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                var i = (r * n) + c;
                var shore = (c > 0 && g.Land[i - 1] != g.Land[i]) || (c < n - 1 && g.Land[i + 1] != g.Land[i]) || (r > 0 && g.Land[i - n] != g.Land[i]) || (r < n - 1 && g.Land[i + n] != g.Land[i]);
                d[i] = shore ? 0f : big;
            }
        }

        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                var i = (r * n) + c;
                if (c > 0) { d[i] = MathF.Min(d[i], d[i - 1] + a); }
                if (r > 0) { d[i] = MathF.Min(d[i], d[i - n] + a); }
                if (r > 0 && c > 0) { d[i] = MathF.Min(d[i], d[i - n - 1] + b); }
                if (r > 0 && c < n - 1) { d[i] = MathF.Min(d[i], d[i - n + 1] + b); }
            }
        }

        for (var r = n - 1; r >= 0; r--)
        {
            for (var c = n - 1; c >= 0; c--)
            {
                var i = (r * n) + c;
                if (c < n - 1) { d[i] = MathF.Min(d[i], d[i + 1] + a); }
                if (r < n - 1) { d[i] = MathF.Min(d[i], d[i + n] + a); }
                if (r < n - 1 && c < n - 1) { d[i] = MathF.Min(d[i], d[i + n + 1] + b); }
                if (r < n - 1 && c > 0) { d[i] = MathF.Min(d[i], d[i + n - 1] + b); }
            }
        }
    }

    /// <summary>Stage 2 per cell: the plain into <c>grid.Height</c> (0–30 m by the coast, up to ~120 m inland), the mountain field into <paramref name="mountain"/>.</summary>
    private static void ReliefRows(WorldGrid g, float[] mountain, int start, int end, ulong key, ReliefArchetype archetype, float rotation, bool mirrored)
    {
        var n = g.Size;
        for (var row = start; row < end; row++)
        {
            for (var col = 0; col < n; col++)
            {
                var i = (row * n) + col;
                if (g.Land[i] == 0) { continue; }
                var u = (2.0 * col / (n - 1)) - 1;
                var v = (2.0 * row / (n - 1)) - 1;
                var (fu, fv) = Frame(u, v, rotation, mirrored);
                var strength = archetype switch
                {
                    ReliefArchetype.Spine => Math.Exp(-Sq((fv - 0.38) / 0.24)),
                    ReliefArchetype.Massif => Math.Exp(-((Sq(fu - 0.22) + Sq(fv - 0.08)) / Sq(0.34))),
                    _ => Math.Max(Math.Exp(-Sq((fv - 0.36) / 0.17)), Math.Exp(-Sq((fv + 0.36) / 0.17))),
                };
                var coast = g.CoastDistM[i];
                var inland = SmoothStep(0, 2200, coast);
                var ridged = WorldNoise.Ridged(key, u * 5.5, v * 5.5, 5);
                var hills = WorldNoise.Fbm(SplitMix64.Mix(key, 0x4111, 0, 0, 0), u * 9, v * 9, 4);
                mountain[i] = (float)(Math.Pow(strength, 1.2) * (0.45 + (0.55 * ridged)) * inland);
                g.Height[i] = (float)((30 * SmoothStep(0, 1500, coast)) + (130 * hills * hills * inland));   // coastal plain → lowland and hills
            }
        }
    }

    /// <summary>W1 islets: land components other than the largest with at least <paramref name="minCells"/> cells (4-connected).</summary>
    private static int Islets(WorldGrid g, float minCells)
    {
        var n = g.Size;
        var label = new int[n * n];
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (var i = 0; i < label.Length; i++)
        {
            if (g.Land[i] == 0 || label[i] != 0) { continue; }
            var id = sizes.Count + 1;
            var count = 0;
            stack.Push(i);
            label[i] = id;
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                count++;
                int r = k / n, c = k % n;
                if (c > 0 && g.Land[k - 1] == 1 && label[k - 1] == 0) { label[k - 1] = id; stack.Push(k - 1); }
                if (c < n - 1 && g.Land[k + 1] == 1 && label[k + 1] == 0) { label[k + 1] = id; stack.Push(k + 1); }
                if (r > 0 && g.Land[k - n] == 1 && label[k - n] == 0) { label[k - n] = id; stack.Push(k - n); }
                if (r < n - 1 && g.Land[k + n] == 1 && label[k + n] == 0) { label[k + n] = id; stack.Push(k + n); }
            }

            sizes.Add(count);
        }

        if (sizes.Count == 0) { return 0; }
        var largest = sizes.Max();
        var skipped = false;
        var islets = 0;
        foreach (var sz in sizes)
        {
            if (sz == largest && !skipped) { skipped = true; continue; }
            if (sz >= minCells) { islets++; }
        }

        return islets;
    }

    private static double Sq(double x) => x * x;

    private static double SmoothStep(double e0, double e1, double x) { var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1); return t * t * (3 - (2 * t)); }
}
