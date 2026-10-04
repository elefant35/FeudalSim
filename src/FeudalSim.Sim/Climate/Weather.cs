using FeudalSim.Sim.Core;
using FeudalSim.Sim.Time;

namespace FeudalSim.Sim.Climate;

/// <summary>10 §6.3 weather states (byte values saved; append only).</summary>
public enum Sky : byte { Clear, Cloudy, Fog, Drizzle, Rain, Storm }

/// <summary>The region-wide weather (10 §6.3): one state per 6-hour slot, the front anomaly, wind, and the year type (§6.4).
/// Packed without padding (saved and hashed as bytes).</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
public struct WeatherState
{
    public long Slot;
    public float Anomaly, WindMs, WindFromDeg;
    public int YearRolled;                // year index + 1 whose types are rolled; 0 = not started
    public Sky Sky;
    public byte SummerType, WinterType;   // 0 Normal · 1 Wet · 2 Dry · 3 Hot  /  0 Normal · 1 Mild · 2 Hard
    public byte Reserved;
}

/// <summary>
/// 10 §6.1–6.4 without snow (M2-03): calendar and daylight, the moon, sea-level temperature with the diurnal swing, lapse
/// rate and coast factor, and a 6-hour Markov weather chain whose stationary shares match §6.3's table (leave weights fitted
/// to the stay probabilities). Each slot's draw is keyed on its index, so the chain is deterministic.
/// </summary>
public static class Weather
{
    public const int SlotMinutes = 360;

    // §6.3 table: Clear, Cloudy, Fog, Drizzle, Rain, Storm — per season.
    private static readonly float[][] Target =
    [
        [0.25f, 0.30f, 0.10f, 0.15f, 0.15f, 0.05f],
        [0.40f, 0.30f, 0.05f, 0.10f, 0.12f, 0.03f],
        [0.20f, 0.28f, 0.15f, 0.15f, 0.15f, 0.07f],
        [0.22f, 0.33f, 0.10f, 0.12f, 0.15f, 0.08f],
    ];

    private static readonly float[] Stay = [0.6f, 0.6f, 0.4f, 0.5f, 0.5f, 0.35f];

    // Fitted leave weights per regime: Spring, Summer, Autumn, Winter, then Summer Wet and Summer Dry (§6.4; Hot = Normal).
    private static readonly float[][] Leave = [.. Enumerable.Range(0, 6).Select(k => Fit(RegimeTarget(k), k))];

    /// <summary>The §6.3 shares a regime should converge to (with the §6.4 Wet/Dry summer shifts).</summary>
    public static float[] RegimeTarget(int regime)
    {
        if (regime < 4) { return Target[regime]; }
        var t = (float[])Target[1].Clone();
        if (regime == 4) { t[0] -= 0.20f; t[3] += 0.10f; t[4] += 0.10f; return t; }   // Wet: Rain/Drizzle +10 each, Clear −20
        var others = 1f - t[0];
        t[0] += 0.20f;
        for (var k = 1; k < 6; k++) { t[k] *= (1f - t[0]) / others; }                // Dry: Clear +20, the rest scaled down
        return t;
    }

    /// <summary>The regime index for a season under the year's summer type.</summary>
    public static int Regime(int season, byte summerType) => season != 1 ? season : summerType switch { 1 => 4, 2 => 5, _ => 1 };

    /// <summary>10 §6.1 day of year 1…32 (Spring 1 = 1).</summary>
    public static int DayOfYear(long gameMinute) => (int)(gameMinute / 1440 % GameDate.DaysPerYear) + 1;

    /// <summary>10 §6.1 daylight hours L(d) = 12 + 4·sin(2π(d − 3)/32).</summary>
    public static float DaylightHours(int d) => 12f + (4f * MathF.Sin(2 * MathF.PI * (d - 3) / 32f));

    public static bool IsDaylight(long gameMinute)
    {
        var l = DaylightHours(DayOfYear(gameMinute));
        var hour = gameMinute % 1440 / 60f;
        return hour >= 12f - (l / 2f) && hour < 12f + (l / 2f);
    }

    /// <summary>Moon phase 0…1 (0 new, 0.5 full): one cycle per season, new on day 1 (10 §6.1).</summary>
    public static float MoonPhase(long gameMinute) => (gameMinute / 1440 % GameDate.DaysPerSeason) / (float)GameDate.DaysPerSeason;

    /// <summary>10 §6.2 sea-level daily mean T̄(d) = 8.5 + 8.5·sin(2π(d − 5)/32) °C.</summary>
    public static float MeanTempC(int d) => 8.5f + (8.5f * MathF.Sin(2 * MathF.PI * (d - 5) / 32f));

    /// <summary>10 §6.2 air temperature at a place: daily mean + year offset + front anomaly + diurnal swing − lapse rate.</summary>
    public static float AirTempC(in WeatherState w, long gameMinute, float elevationM = 0f, bool nearCoast = true)
    {
        var d = DayOfYear(gameMinute);
        var season = (int)((d - 1) / GameDate.DaysPerSeason);
        var amplitude = season switch { 0 => 4f, 1 => 5f, 2 => 3.5f, _ => 3f };
        var cloud = w.Sky switch { Sky.Clear => 1.3f, Sky.Cloudy => 0.7f, Sky.Fog => 0.6f, _ => 0.5f };
        var hour = gameMinute % 1440 / 60f;
        return MeanTempC(d) + YearOffset(w, season) + w.Anomaly + (amplitude * MathF.Cos(2 * MathF.PI * (hour - 15f) / 24f) * cloud * (nearCoast ? 0.7f : 1f))
               - (6.5f * elevationM / 1000f);
    }

    /// <summary>Precipitation (mm/h) for the sky (10 §6.3 table); Dry summers × 0.4.</summary>
    public static float PrecipMmH(in WeatherState w) => w.Sky switch { Sky.Fog => 0.03f, Sky.Drizzle => 0.2f, Sky.Rain => 1f, Sky.Storm => 3f, _ => 0f }
                                                        * (w.SummerType == 2 ? 0.4f : 1f);

    public static bool Wet(in WeatherState w) => w.Sky is Sky.Drizzle or Sky.Rain or Sky.Storm;

    private static float YearOffset(in WeatherState w, int season)
        => season == 1 && w.SummerType == 3 ? 2.5f : season == 3 ? w.WinterType switch { 1 => 2f, 2 => -3f, _ => 0f } : 0f;

    /// <summary>Advances the state to <paramref name="slot"/> (one step per slot; deterministic per slot index).</summary>
    public static void Advance(ref WeatherState w, ulong worldSeed, long slot)
    {
        var rng = new Rng(SplitMix64.Mix(worldSeed, (ulong)RngStream.Weather, (ulong)slot, 0, 0));
        var minute = slot * SlotMinutes;
        var d = DayOfYear(minute);
        var season = (d - 1) / GameDate.DaysPerSeason;
        var year = (int)(minute / 1440 / GameDate.DaysPerYear);
        var first = w.YearRolled == 0;
        if (w.YearRolled != year + 1)   // §6.4: rolled on each Spring 1
        {
            var yr = new Rng(SplitMix64.Mix(worldSeed, (ulong)RngStream.Weather, 0xFEA7, (ulong)year, 0));
            var s = yr.NextFloat01();
            w.SummerType = (byte)(s < 0.60f ? 0 : s < 0.80f ? 1 : s < 0.95f ? 2 : 3);
            var v = yr.NextFloat01();
            w.WinterType = (byte)(v < 0.25f ? 1 : v < 0.80f ? 0 : 2);
            if (year == 0 && w.WinterType == 2) { w.WinterType = 0; }   // Y0 Winter capped at Normal
            w.YearRolled = year + 1;
        }

        var slotOfDay = (int)(slot % 4);
        var regime = Regime(season, w.SummerType);
        Span<float> row = stackalloc float[6];
        Sky next;
        if (first) { next = Sample(RegimeTarget(regime), rng.NextFloat01()); }
        else
        {
            Row(w.Sky, slotOfDay, Leave[regime], regime, row);
            next = Sample(row, rng.NextFloat01());
        }

        // AR(1) front anomaly per slot; wind from the state's band, mostly westerly.
        var sigma = season switch { 1 => 1.0f, 3 => 1.5f, _ => 1.2f };
        w.Anomaly = (0.85f * w.Anomaly) + (sigma * rng.NextNormal());
        var (lo, hi) = next switch { Sky.Clear => (2f, 5f), Sky.Cloudy => (3f, 7f), Sky.Fog => (0f, 2f), Sky.Drizzle => (2f, 6f), Sky.Rain => (4f, 9f), _ => (12f, 22f) };
        w.WindMs = rng.Uniform(lo, hi);
        w.WindFromDeg = 247.5f + rng.Normal(0f, 35f);
        (w.Sky, w.Slot) = (next, slot);
    }

    /// <summary>
    /// The §6.3 transition row from <paramref name="from"/> at a slot of the day: stay with p_stay, else a draw ∝ the leave
    /// weights over the other states. Storm only follows Cloudy, Drizzle or Rain; Fog only forms in the 00 and 06 slots and
    /// (except in Autumn) burns off at 12 with p 0.7 and is gone by 18. Shared by the chain and the fit, so the test is exact.
    /// </summary>
    public static void Row(Sky from, int slotOfDay, ReadOnlySpan<float> leave, int regime, Span<float> row)
    {
        var i = (int)from;
        var autumn = regime == 2;
        var stay = StayP(i, regime);
        var total = 0f;
        for (var j = 0; j < 6; j++) { total += CanEnter(i, j, slotOfDay) ? leave[j] : 0f; }
        for (var j = 0; j < 6; j++) { row[j] = j == i ? stay : CanEnter(i, j, slotOfDay) && total > 0f ? (1f - stay) * leave[j] / total : 0f; }
        if (total <= 0f) { row[i] = 1f; }
        if (from == Sky.Fog && !autumn && slotOfDay >= 2)
        {
            var burn = slotOfDay == 2 ? 0.7f * row[i] : row[i];
            row[i] -= burn;
            row[(int)Sky.Cloudy] += burn;
        }
    }

    /// <summary>p_stay; a Dry summer holds Clear at 0.75 (finding 10 Q17: 60% Clear is unreachable at 0.6, max ≈ 56%).</summary>
    public static float StayP(int state, int regime) => regime == 5 && state == (int)Sky.Clear ? 0.75f : Stay[state];

    private static bool CanEnter(int from, int to, int slotOfDay)
        => to != from
           && (to != (int)Sky.Storm || from is (int)Sky.Cloudy or (int)Sky.Drizzle or (int)Sky.Rain)
           && (to != (int)Sky.Fog || slotOfDay < 2);

    private static Sky Sample(ReadOnlySpan<float> p, float u)
    {
        var total = 0f;
        for (var k = 0; k < p.Length; k++) { total += p[k]; }
        var x = u * total;
        for (var k = 0; k < p.Length; k++)
        {
            if (x < p[k]) { return (Sky)k; }
            x -= p[k];
        }

        return Sky.Cloudy;
    }

    /// <summary>The long-run share of each state under a regime's chain (mean over the four slots of the day).</summary>
    public static double[] StationaryShares(int regime) => Stationary(Leave[regime], regime, null, cycles: 400);

    /// <summary>Fits leave weights so the periodic chain's stationary shares match the target (multiplicative updates).</summary>
    private static float[] Fit(float[] target, int regime)
    {
        var r = target.ToArray();
        var pi = (double[])[.. target.Select(t => (double)t)];
        for (var it = 0; it < 400; it++)
        {
            pi = Stationary(r, regime, pi, cycles: 6);   // warm-started: a few cycles per update keep the fit ≈ 10 ms
            for (var k = 0; k < 6; k++) { r[k] = (float)(r[k] * Math.Pow(target[k] / Math.Max(1e-9, pi[k]), 0.6)); }
            var sum = r.Sum();
            for (var k = 0; k < 6; k++) { r[k] /= sum; }
        }

        return r;
    }

    private static double[] Stationary(float[] leave, int regime, double[]? start, int cycles)
    {
        Span<float> row = stackalloc float[6];
        var pi = new double[6];
        if (start is null) { Array.Fill(pi, 1.0 / 6); } else { Array.Copy(start, pi, 6); }
        var mean = new double[6];
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            for (var s = 0; s < 4; s++)
            {
                var next = new double[6];
                for (var i = 0; i < 6; i++)
                {
                    Row((Sky)i, s, leave, regime, row);
                    for (var j = 0; j < 6; j++) { next[j] += pi[i] * row[j]; }
                }

                pi = next;
                if (cycle == cycles - 1) { for (var k = 0; k < 6; k++) { mean[k] += pi[k] / 4; } }
            }
        }

        return mean;
    }
}
