using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.WorldGen;

/// <summary>10 §3.5 water source classes handed to 11 (byte values saved; append only).</summary>
public enum WaterClass : byte { None, Spring, Stream, River, Lake, MarshPool, Brackish, Sea }

public sealed record Lake(int Id, int Cells, float AreaHa, float LevelM, float VolumeM3);

public sealed record River(int OutletCell, float CatchmentKm2, int LengthCells);

/// <summary>
/// 10 §3.2 stage 5 (10 §3.5): priority-flood depression fill (lakes kept by volume and area, the rest filled), an
/// epsilon-filled surface so every land cell drains, D8 flow directions and accumulation, streams and rivers carved at
/// 10 §3.5's widths and depths, ford candidates, springs, water classes, and peat where the ground stays wet.
/// Ordered algorithms throughout — serial and deterministic (priority ties broken by cell index).
/// </summary>
public static class Hydrology
{
    public const float StreamKm2 = 0.5f, RiverKm2 = 4f, LakeMinHa = 2f, LakeMinM3 = 20_000f;
    private static readonly int[] Dc = [1, 1, 0, -1, -1, -1, 0, 1];
    private static readonly int[] Dr = [0, 1, 1, 1, 0, -1, -1, -1];

    public sealed record Result(IReadOnlyList<Lake> Lakes, IReadOnlyList<River> Rivers, IReadOnlyList<int> Fords, IReadOnlyList<int> Springs, float[] AccumKm2, sbyte[] Flow)
    {
        /// <summary>Catchments (km²) of every land cell that drains straight into the sea, largest first.</summary>
        public IReadOnlyList<float> Outlets { get; init; } = [];
    }

    public static Result Run(WorldGrid g, ulong key)
    {
        var n = g.Size;
        var h = g.Height;
        var cellM2 = g.CellM * g.CellM;

        // 1. Priority flood from the sea: filled surface F (lakes are F > H).
        var filled = PriorityFill(g, epsilon: 0f);
        var lakeId = new int[n * n];
        var lakes = new List<Lake>();
        var stack = new Stack<int>();
        for (var i = 0; i < h.Length; i++)
        {
            if (g.Land[i] == 0 || lakeId[i] != 0 || filled[i] - h[i] < 0.05f) { continue; }
            var id = lakes.Count + 1;
            int cells = 0;
            double volume = 0;
            var members = new List<int>();
            stack.Push(i);
            lakeId[i] = id;
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                members.Add(k);
                cells++;
                volume += (filled[k] - h[k]) * cellM2;
                foreach (var j in Neighbours4(k, n))
                {
                    if (g.Land[j] == 1 && lakeId[j] == 0 && filled[j] - h[j] >= 0.05f && Math.Abs(filled[j] - filled[k]) < 0.01f) { lakeId[j] = id; stack.Push(j); }
                }
            }

            var areaHa = cells * cellM2 / 1e4f;
            var level = filled[i];
            if (areaHa >= LakeMinHa && volume >= LakeMinM3)
            {
                lakes.Add(new Lake(id, cells, areaHa, level, (float)volume));
                foreach (var k in members) { g.Water[k] = (byte)WaterClass.Lake; }
            }
            else
            {
                lakes.Add(new Lake(id, cells, areaHa, level, (float)volume) with { Id = -id });   // filled (kept for ids)
                foreach (var k in members) { h[k] = filled[k]; }   // §3.5: smaller depressions are filled
            }
        }

        var kept = lakes.Where(l => l.Id > 0).ToList();

        // 2. Epsilon fill so flats and lakes drain, then D8 and accumulation (high to low).
        var routed = PriorityFill(g, epsilon: 1e-3f);
        var flow = new sbyte[n * n];
        var order = new int[n * n];
        for (var i = 0; i < order.Length; i++) { order[i] = i; }
        Array.Sort(order, (a, b) => routed[b] != routed[a] ? routed[b].CompareTo(routed[a]) : a.CompareTo(b));
        var accum = new float[n * n];
        foreach (var i in order)
        {
            if (g.Land[i] == 0) { flow[i] = -1; continue; }
            int r = i / n, c = i % n;
            var best = -1;
            var bestDrop = 0f;
            for (var d = 0; d < 8; d++)
            {
                int rr = r + Dr[d], cc = c + Dc[d];
                if (rr < 0 || cc < 0 || rr >= n || cc >= n) { continue; }
                var drop = (routed[i] - routed[(rr * n) + cc]) / (d % 2 == 0 ? 1f : 1.41421356f);
                if (drop > bestDrop) { (bestDrop, best) = (drop, d); }
            }

            flow[i] = (sbyte)best;
            accum[i] += cellM2 / 1e6f;
            if (best >= 0) { accum[((r + Dr[best]) * n) + c + Dc[best]] += accum[i]; }
        }

        // 3. Channels: carve streams and rivers (§3.5 width/depth), classify water, find river outlets and lengths.
        var rivers = new List<River>();
        var riverCells = new int[n * n];
        for (var i = 0; i < h.Length; i++)
        {
            if (g.Land[i] == 0 || accum[i] < StreamKm2 || g.Water[i] == (byte)WaterClass.Lake) { continue; }
            var a = accum[i];
            var depth = 0.3f + (0.25f * MathF.Sqrt(a));
            var width = 2f + (6f * MathF.Sqrt(a));
            g.Water[i] = (byte)(a >= RiverKm2 ? WaterClass.River : WaterClass.Stream);
            h[i] -= depth;
            var radius = (int)(width / 2 / g.CellM);
            int r = i / n, c = i % n;
            for (var dr = -radius; dr <= radius; dr++)
            {
                for (var dc = -radius; dc <= radius; dc++)
                {
                    int rr = r + dr, cc = c + dc;
                    if ((dr == 0 && dc == 0) || rr < 0 || cc < 0 || rr >= n || cc >= n) { continue; }
                    var j = (rr * n) + cc;
                    if (g.Land[j] == 1 && g.Water[j] == (byte)WaterClass.None) { g.Water[j] = g.Water[i]; h[j] = MathF.Min(h[j], h[i] + (depth * 0.5f)); }
                }
            }

            var down = flow[i] < 0 ? -1 : ((r + Dr[flow[i]]) * n) + c + Dc[flow[i]];
            if (a >= RiverKm2 && (down < 0 || g.Land[down] == 0)) { rivers.Add(new River(i, a, 0)); }
        }

        // 4. Fords: river reaches with slope 0.3–1.5%, at most one per 300 m along a channel (§3.5, §12.4).
        var fords = new List<int>();
        var lastFord = new Dictionary<int, int>();
        foreach (var i in order)
        {
            if (g.Water[i] != (byte)WaterClass.River || flow[i] < 0) { continue; }
            int r = i / n, c = i % n;
            var down = ((r + Dr[flow[i]]) * n) + c + Dc[flow[i]];
            var slope = (routed[i] - routed[down]) / g.CellM;
            if (slope is < 0.003f or > 0.015f) { continue; }
            var tooClose = fords.Any(f => Dist(f, i, n) * g.CellM < 300f);
            if (!tooClose) { fords.Add(i); }
        }

        // 5. Springs: concave hill-foot cells (a slope break) on permeable rock, 6–15 of them (§3.5).
        var springs = new List<int>();
        var rng = new Rng(key);
        var candidates = new List<int>();
        for (var r = 4; r < n - 4; r += 3)
        {
            for (var c = 4; c < n - 4; c += 3)
            {
                var i = (r * n) + c;
                if (g.Land[i] == 0 || g.Water[i] != 0 || h[i] < 10f) { continue; }
                var lith = (Lithology)g.Lithology[i];
                if (lith is not (Lithology.Chalk or Lithology.Sandstone or Lithology.Metamorphic)) { continue; }
                var lap = h[i - 4] + h[i + 4] + h[i - (4 * n)] + h[i + (4 * n)] - (4 * h[i]);   // > 0: concave hill foot
                var upslope = MathF.Max(MathF.Max(Lithologies.SlopeDeg(g, c, r - 4, 2), Lithologies.SlopeDeg(g, c, r + 4, 2)), MathF.Max(Lithologies.SlopeDeg(g, c - 4, r, 2), Lithologies.SlopeDeg(g, c + 4, r, 2)));
                if (lap > 3f && upslope > 6f && Lithologies.SlopeDeg(g, c, r, 2) < upslope - 3f) { candidates.Add(i); }   // a slope break
            }
        }

        var wanted = rng.Range(6, 16);
        while (springs.Count < wanted && candidates.Count > 0)
        {
            var pick = candidates[rng.Range(0, candidates.Count)];
            candidates.Remove(pick);
            if (springs.All(s => Dist(s, pick, n) * g.CellM > 400f)) { springs.Add(pick); g.Water[pick] = (byte)WaterClass.Spring; }
        }

        // 6. Peat (10 §3.4: wetland and wet moor): flat ground with a large upslope area — the topographic wetness index.
        for (var i = 0; i < h.Length; i++)
        {
            if (g.Land[i] == 0 || g.Water[i] != 0) { continue; }
            int r = i / n, c = i % n;
            var slope = MathF.Max(0.001f, MathF.Tan(Lithologies.SlopeDeg(g, c, r, 2) * MathF.PI / 180f));
            var twi = MathF.Log(accum[i] * 1e6f / g.CellM / slope);
            if (twi > 12.5f && (Lithology)g.Lithology[i] is not (Lithology.Granite or Lithology.Chalk))
            {
                g.Lithology[i] = (byte)Lithology.Peat;
                if (twi > 14f && h[i] < 60f) { g.Water[i] = (byte)WaterClass.MarshPool; }
            }
        }

        for (var i = 0; i < h.Length; i++) { if (g.Land[i] == 0) { g.Water[i] = (byte)WaterClass.Sea; } }
        var outlets = new List<float>();
        for (var i = 0; i < h.Length; i++)
        {
            if (g.Land[i] == 0 || flow[i] < 0) { continue; }
            var down = (((i / n) + Dr[flow[i]]) * n) + (i % n) + Dc[flow[i]];
            if (g.Land[down] == 0) { outlets.Add(accum[i]); }
        }

        outlets.Sort((a, b) => b.CompareTo(a));
        return new Result(kept, rivers, fords, springs, accum, flow) { Outlets = outlets };
    }

    /// <summary>Priority-flood (Barnes et al. 2014) from the sea and the region edge; <paramref name="epsilon"/> &gt; 0 makes flats drain.</summary>
    private static float[] PriorityFill(WorldGrid g, float epsilon)
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

        while (pq.TryDequeue(out var i, out var p))
        {
            foreach (var j in Neighbours8(i, n))
            {
                if (done[j]) { continue; }
                done[j] = true;
                f[j] = MathF.Max(h[j], p.H + epsilon);
                pq.Enqueue(j, (f[j], j));
            }
        }

        return f;
    }

    private static IEnumerable<int> Neighbours4(int i, int n)
    {
        int r = i / n, c = i % n;
        if (c > 0) { yield return i - 1; }
        if (c < n - 1) { yield return i + 1; }
        if (r > 0) { yield return i - n; }
        if (r < n - 1) { yield return i + n; }
    }

    private static IEnumerable<int> Neighbours8(int i, int n)
    {
        int r = i / n, c = i % n;
        for (var d = 0; d < 8; d++)
        {
            int rr = r + Dr[d], cc = c + Dc[d];
            if (rr >= 0 && cc >= 0 && rr < n && cc < n) { yield return (rr * n) + cc; }
        }
    }

    private static float Dist(int a, int b, int n)
    {
        float dr = (a / n) - (b / n), dc = (a % n) - (b % n);
        return MathF.Sqrt((dr * dr) + (dc * dc));
    }
}
