using FeudalSim.Sim.World;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Hosting;

/// <summary>
/// M2-FP1: puts a scenario's camp on the generated island. The camp's places, the settlers' spawn grid and the player are
/// written in camp-local metres (the M1 flat camp); with <c>camp.anchor: landing</c> they move so the fire sits on dry,
/// gentle ground just above the landing beach (11 §15: the beach camp of Landfall, with the wreck in sight). Each place
/// that would land in the sea or a lake is pulled back towards the fire until it is on land; the water place snaps to
/// the nearest fresh-water cell (spring, stream, river or lake shore) within <see cref="WaterSnapM"/>. Woods and forage
/// stay where the offsets put them: M2-FP3 replaces them with the real nodes. Pure function of the map: the result goes
/// into the logged spawn commands and the saved camp record, so replays and saves are unaffected.
/// </summary>
public static class CampAnchor
{
    /// <summary>The fire's distance from the shore: above the strand, where 11 §15's beach camp stands.</summary>
    public const float MinCoastM = 30f, MaxCoastM = 120f, MaxSlopeDeg = 8f;

    /// <summary>10 §3.9's landing guarantee is fresh water ≤ 400 m from the beach; the camp sits a little inland of it.</summary>
    public const float WaterSnapM = 600f;

    /// <param name="WaterKind">11 §11.1 source kind of the snapped water (spring, stream, river, lake; a brook counts as a stream).</param>
    public sealed record Result(float Dx, float Dz, float FireX, float FireZ, float ElevationM, Dictionary<string, float[]> Places, bool WaterSnapped, string WaterKind = "stream");

    public static Result? Resolve(WorldMap map, CampDef camp)
    {
        if (map.Landing is not { } landing) { return null; }
        var g = map.Grid;
        var fireCell = Inland(g, landing.BeachX, landing.BeachZ);
        if (fireCell < 0) { return null; }
        var n = g.Size;
        float fx = g.X(fireCell % n), fz = g.Z(fireCell / n);
        var local = camp.Places.TryGetValue("fire", out var f) && f.Length == 2 ? (f[0], f[1]) : (0f, 0f);
        float dx = fx - local.Item1, dz = fz - local.Item2;

        var places = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var (key, p) in camp.Places.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (p.Length != 2) { continue; }
            places[key] = OnLand(g, p[0] + dx, p[1] + dz, fx, fz);
        }

        var snapped = false;
        var kind = "stream";
        if ((NearestFresh(g, fx, fz) ?? NearestBrook(g, fx, fz)) is { } water)
        {
            places["water"] = [water.X, water.Z];
            snapped = true;
            kind = Sim.Survival.Water.KindAt(g, water.X, water.Z) ?? "stream";
        }

        return new Result(dx, dz, fx, fz, MathF.Max(0.5f, g.Height[fireCell]), places, snapped, kind);
    }

    /// <summary>
    /// From the beach cell, the nearest land cell (by distance from the beach) with coast distance in
    /// [<see cref="MinCoastM"/>, <see cref="MaxCoastM"/>], slope ≤ <see cref="MaxSlopeDeg"/>, not in water. −1 if none
    /// within 600 m.
    /// </summary>
    public static int Inland(WorldGrid g, float beachX, float beachZ)
    {
        var n = g.Size;
        var half = (n - 1) * g.CellM / 2f;
        int bc = (int)MathF.Round((beachX + half) / g.CellM), br = (int)MathF.Round((beachZ + half) / g.CellM);
        var reach = (int)(600f / g.CellM);
        var best = -1;
        var bestD = float.MaxValue;
        for (var r = Math.Max(1, br - reach); r <= Math.Min(n - 2, br + reach); r++)
        {
            for (var c = Math.Max(1, bc - reach); c <= Math.Min(n - 2, bc + reach); c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0 || g.Water[i] != 0 || g.Slope[i] > MaxSlopeDeg) { continue; }
                if (g.CoastDistM[i] < MinCoastM || g.CoastDistM[i] > MaxCoastM) { continue; }
                float ddx = c - bc, ddz = r - br;
                var d = (ddx * ddx) + (ddz * ddz);
                if (d < bestD) { (best, bestD) = (i, d); }   // row-major scan, strict <: ties keep the first (deterministic)
            }
        }

        return best;
    }

    /// <summary>Pulls a point towards the fire in 4 m steps until its 8 m cell is dry land.</summary>
    private static float[] OnLand(WorldGrid g, float x, float z, float fx, float fz)
    {
        var d = MathF.Sqrt(((x - fx) * (x - fx)) + ((z - fz) * (z - fz)));
        for (var t = 0f; t <= d; t += 4f)
        {
            var k = d <= 0f ? 0f : 1f - (t / d);
            float px = fx + ((x - fx) * k), pz = fz + ((z - fz) * k);
            var i = Cell(g, px, pz);
            if (i >= 0 && g.Land[i] == 1 && (WaterClass)g.Water[i] is not (WaterClass.Lake or WaterClass.Sea or WaterClass.Brackish or WaterClass.River)) { return [px, pz]; }
        }

        return [fx, fz];
    }

    /// <summary>The nearest spring, stream or river cell (or a land cell beside a lake) within <see cref="WaterSnapM"/>; the point is on its bank.</summary>
    public static (float X, float Z)? NearestFresh(WorldGrid g, float fx, float fz)
    {
        var n = g.Size;
        var half = (n - 1) * g.CellM / 2f;
        int fc = (int)MathF.Round((fx + half) / g.CellM), fr = (int)MathF.Round((fz + half) / g.CellM);
        var reach = (int)(WaterSnapM / g.CellM);
        (float, float)? best = null;
        var bestD = float.MaxValue;
        for (var r = Math.Max(1, fr - reach); r <= Math.Min(n - 2, fr + reach); r++)
        {
            for (var c = Math.Max(1, fc - reach); c <= Math.Min(n - 2, fc + reach); c++)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0) { continue; }
                var w = (WaterClass)g.Water[i];
                var fresh = w is WaterClass.Spring or WaterClass.Stream || (w == WaterClass.None && (Neighbour(g, i, WaterClass.Lake) || Neighbour(g, i, WaterClass.River)));
                if (!fresh) { continue; }
                float ddx = c - fc, ddz = r - fr;
                var d = (ddx * ddx) + (ddz * ddz);
                if (d < bestD && d * g.CellM * g.CellM <= WaterSnapM * WaterSnapM) { (best, bestD) = ((g.X(c), g.Z(r)), d); }
            }
        }

        return best;
    }

    /// <summary>
    /// 10 §3.9 counts brooks (≥ <see cref="Landing.BrookKm2"/> of catchment reaching the shore without a channel class)
    /// as fresh water, but the saved grids keep no accumulation. This recomputes it as stage 5 does: a priority flood from
    /// the sea with a small ε so flats drain (ties by index), then D8 accumulation from the highest filled cell down, and
    /// returns the nearest land cell (above 0.5 m) on such a brook within <see cref="WaterSnapM"/>. ≈ 0.5 s for 1,025².
    /// </summary>
    public static (float X, float Z)? NearestBrook(WorldGrid g, float fx, float fz)
    {
        var n = g.Size;
        var h = g.Height;
        var f = new float[n * n];
        var done = new bool[n * n];
        var pq = new PriorityQueue<int, (float H, int I)>();
        for (var i = 0; i < h.Length; i++)
        {
            int r = i / n, c = i % n;
            if (g.Land[i] == 0 || r == 0 || c == 0 || r == n - 1 || c == n - 1) { f[i] = h[i]; done[i] = true; pq.Enqueue(i, (h[i], i)); }
        }

        var order = new List<int>(n * n);
        while (pq.TryDequeue(out var i, out var p))
        {
            order.Add(i);
            for (var dr = -1; dr <= 1; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    int r = (i / n) + dr, c = (i % n) + dc;
                    if ((dr == 0 && dc == 0) || r < 0 || c < 0 || r >= n || c >= n) { continue; }
                    var j = (r * n) + c;
                    if (done[j]) { continue; }
                    done[j] = true;
                    f[j] = MathF.Max(h[j], p.H + 0.001f);
                    pq.Enqueue(j, (f[j], j));
                }
            }
        }

        // The flood pops cells low to high, so walking that order backwards visits every cell before the one it drains to.
        var accum = new float[n * n];
        var cellKm2 = g.CellM * g.CellM / 1e6f;
        for (var k = order.Count - 1; k >= 0; k--)
        {
            var i = order[k];
            if (g.Land[i] == 0) { continue; }
            accum[i] += cellKm2;
            int r = i / n, c = i % n;
            var low = -1;
            var drop = 0f;
            for (var dr = -1; dr <= 1; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    int rr = r + dr, cc = c + dc;
                    if ((dr == 0 && dc == 0) || rr < 0 || cc < 0 || rr >= n || cc >= n) { continue; }
                    var j = (rr * n) + cc;
                    var d = (f[i] - f[j]) / ((dr != 0 && dc != 0) ? 1.4142135f : 1f);
                    if (d > drop) { (low, drop) = (j, d); }
                }
            }

            if (low >= 0) { accum[low] += accum[i]; }
        }

        var half = (n - 1) * g.CellM / 2f;
        int fc = (int)MathF.Round((fx + half) / g.CellM), fr = (int)MathF.Round((fz + half) / g.CellM);
        var reach = (int)(WaterSnapM / g.CellM);
        (float, float)? best = null;
        var bestD = float.MaxValue;
        for (var r = Math.Max(1, fr - reach); r <= Math.Min(n - 2, fr + reach); r++)
        {
            for (var c = Math.Max(1, fc - reach); c <= Math.Min(n - 2, fc + reach); c++)
            {
                var i = (r * n) + c;
                if (accum[i] < Landing.BrookKm2 || g.Land[i] == 0 || h[i] <= 0.5f) { continue; }
                float ddx = c - fc, ddz = r - fr;
                var d = (ddx * ddx) + (ddz * ddz);
                if (d < bestD && d * g.CellM * g.CellM <= WaterSnapM * WaterSnapM) { (best, bestD) = ((g.X(c), g.Z(r)), d); }
            }
        }

        return best;
    }

    private static bool Neighbour(WorldGrid g, int i, WaterClass w)
        => (WaterClass)g.Water[i - 1] == w || (WaterClass)g.Water[i + 1] == w || (WaterClass)g.Water[i - g.Size] == w || (WaterClass)g.Water[i + g.Size] == w;

    public static int Cell(WorldGrid g, float x, float z)
    {
        var half = (g.Size - 1) * g.CellM / 2f;
        int c = (int)MathF.Round((x + half) / g.CellM), r = (int)MathF.Round((z + half) / g.CellM);
        return c < 0 || r < 0 || c >= g.Size || r >= g.Size ? -1 : (r * g.Size) + c;
    }
}
