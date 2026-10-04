using FeudalSim.Sim.Core;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim;

/// <summary>
/// Which people update this step, and over how much game time (20 §5.2, 21 §15.1). Built once per step after the Sense
/// phase (so LOD changes apply at once) and read by every per-person system, which then iterate only the due rows:
/// <list type="bullet">
/// <item>LOD0 / LOD1: every step (M1: LOD1 still runs at the full 10 Hz; at ≈ 1 µs per person-step it is inside the
/// 30 µs LOD1 budget, so 20 §5.2's 1 Hz bucketing waits until profiling needs it — S6).</item>
/// <item>LOD2: once per game hour, at a per-person phase offset <c>hash(id) mod 1 h</c> (20 §5.2), so the hourly work is
/// spread across the hour and independent of day length.</item>
/// <item>LOD3: once per game day, at the day boundary (M1 has one settlement: slice 0).</item>
/// </list>
/// Each due row gets its own <c>dt = now − LastUpdateGameMs</c>, so a person promoted or demoted integrates exactly the
/// time since its last update. Not state: rebuilt from the LOD column and the clock every step.
/// </summary>
public sealed class TierSchedule
{
    private int[] _rows = new int[64];
    private long[] _dt = new long[64];
    private int _count;
    private int _lod2Due, _lod3Due;

    /// <summary>The due rows in id order.</summary>
    public ReadOnlySpan<int> Rows => _rows.AsSpan(0, _count);

    /// <summary>Game-ms to integrate for the k-th due row.</summary>
    public long Dt(int k) => _dt[k];

    public int Count => _count;

    /// <summary>LOD2 / LOD3 rows due this step (diagnostics).</summary>
    public int Lod2Due => _lod2Due;

    public int Lod3Due => _lod3Due;

    /// <summary>The LOD2 phase offset of a person within the game hour (20 §5.2).</summary>
    public static long Lod2Offset(EntityId id) => (long)(SplitMix64.Avalanche(id.Value ^ 0x10D2_0000_0000_0000UL) % (ulong)Time.SimClock.MsPerGameHour);

    internal void Build(SimWorld world, long gameMs, long dtGameMs)
    {
        var people = world.People;
        if (_rows.Length < people.Count)
        {
            Array.Resize(ref _rows, Math.Max(people.Count, _rows.Length * 2));
            Array.Resize(ref _dt, _rows.Length);
        }

        _count = _lod2Due = _lod3Due = 0;
        var prev = gameMs - dtGameMs;
        var lod = people.Lod;
        var vitals = people.Vitals;
        for (var i = 0; i < people.Count; i++)
        {
            if (vitals[i].Dead) { continue; }   // 11 §14: a body integrates nothing
            ref var l = ref lod[i];
            bool due;
            switch (l.Tier)
            {
                case LodTier.Lod2:
                    // Due when (offset + k·hour) falls in (prev, now] for some k.
                    var offset = Lod2Offset(people.Ids[i]);
                    due = dtGameMs >= Time.SimClock.MsPerGameHour || Floor(gameMs - offset) != Floor(prev - offset);
                    if (due) { _lod2Due++; }
                    break;

                case LodTier.Lod3:
                    due = gameMs / Time.SimClock.MsPerGameDay != prev / Time.SimClock.MsPerGameDay;
                    if (due) { _lod3Due++; }
                    break;

                default:
                    due = true;
                    break;
            }

            if (!due) { continue; }
            _rows[_count] = i;
            _dt[_count++] = Math.Max(0, gameMs - l.LastUpdateGameMs);
            l.LastUpdateGameMs = gameMs;
        }

        static long Floor(long x) => x >= 0 ? x / Time.SimClock.MsPerGameHour : ((x + 1) / Time.SimClock.MsPerGameHour) - 1;
    }
}
