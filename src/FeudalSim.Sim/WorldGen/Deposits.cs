using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>T0 deposit kinds placed at generation (M2-01b-ii; metals arrive with mining, M3–M4).</summary>
public enum DepositKind : byte { FlintBed, ClayPit, Quarry }

/// <summary>10 §3.12 deposit (sketch): where, how big (radius), grade 0–1 and how hard it is to notice (10 §5.3).</summary>
public sealed record Deposit(int Id, DepositKind Kind, float X, float Z, float RadiusM, float Grade, int DetectDifficulty);

/// <summary>
/// 10 §3.8 placement for the T0 deposits: candidates by rule, a score with 0.2 noise, highest first under Poisson-disk
/// spacing, counts drawn from 10 §5.1's ranges — flint beds 3–8 on chalk (cliffs and coast score higher), clay pits 4–10
/// on valley banks and wetland edges, quarries 2–5 on steep sandstone or chalk.
/// </summary>
public static class Deposits
{
    public static IReadOnlyList<Deposit> Place(WorldGrid g, ulong key)
    {
        var rng = new Rng(key);
        var placed = new List<Deposit>();
        Place(g, rng.Range(3, 9), 500f, DepositKind.FlintBed, key, placed, i => (Lithology)g.Lithology[i] == Lithology.Chalk ? (g.CoastDistM[i] < 300f ? 1f : 0.5f) : -1f,
            ref rng, (30f, 60f), 10);
        Place(g, rng.Range(4, 11), 400f, DepositKind.ClayPit, key, placed,
            i => ((Biome)g.Biome[i] is Biome.RiverValley or Biome.Wetland || (Lithology)g.Lithology[i] is Lithology.Alluvium) && NearWater(g, i) ? ((Biome)g.Biome[i] == Biome.RiverValley ? 1f : 0.7f) : -1f,
            ref rng, (20f, 50f), 15);
        Place(g, rng.Range(2, 6), 800f, DepositKind.Quarry, key, placed,
            i => (Lithology)g.Lithology[i] is Lithology.Sandstone or Lithology.Chalk && g.Slope[i] >= 15 ? 0.5f + (g.Slope[i] / 90f) : -1f,
            ref rng, (25f, 60f), 5);
        return placed;
    }

    private static void Place(WorldGrid g, int count, float spacingM, DepositKind kind, ulong key, List<Deposit> placed, Func<int, float> rule, ref Rng rng, (float Lo, float Hi) radius, int detect)
    {
        var candidates = new List<(float Score, int Cell)>();
        for (var i = 0; i < g.Height.Length; i++)
        {
            if (g.Land[i] == 0) { continue; }
            var s = rule(i);
            if (s < 0f) { continue; }
            var noise = (SplitMix64.Mix(key, (ulong)kind, (ulong)i, 0, 0) & 0xFFFF) / 65536f;
            candidates.Add((s + (0.2f * noise), i));
        }

        candidates.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Cell.CompareTo(b.Cell));
        var n = g.Size;
        var mine = 0;
        foreach (var (_, cell) in candidates)
        {
            if (mine >= count) { break; }
            float x = g.X(cell % n), z = g.Z(cell / n);
            if (placed.Any(d => d.Kind == kind && (((d.X - x) * (d.X - x)) + ((d.Z - z) * (d.Z - z))) < spacingM * spacingM)) { continue; }
            placed.Add(new Deposit(placed.Count + 1, kind, x, z, rng.Uniform(radius.Lo, radius.Hi), rng.Uniform(0.4f, 0.95f), detect));
            mine++;
        }
    }

    private static bool NearWater(WorldGrid g, int i)
    {
        var n = g.Size;
        int r = i / n, c = i % n;
        for (var dr = -12; dr <= 12; dr += 3)
        {
            for (var dc = -12; dc <= 12; dc += 3)
            {
                int rr = r + dr, cc = c + dc;
                if (rr < 0 || rr >= n || cc < 0 || cc >= n) { continue; }
                if ((WaterClass)g.Water[(rr * n) + cc] is WaterClass.River or WaterClass.Stream or WaterClass.Lake) { return true; }
            }
        }

        return false;
    }
}
