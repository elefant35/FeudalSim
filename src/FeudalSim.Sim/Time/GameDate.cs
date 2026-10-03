namespace FeudalSim.Sim.Time;

public enum Season : byte { Spring, Summer, Autumn, Winter }

/// <summary>
/// Calendar per canon §6: 8-day seasons, 32-day years, Hearthday = day 8. Day-of-year is 1…32.
/// </summary>
public readonly record struct GameDate(int Year, Season Season, int Day, int Hour, int Minute)
{
    public const int DaysPerSeason = 8;
    public const int SeasonsPerYear = 4;
    public const int DaysPerYear = DaysPerSeason * SeasonsPerYear;
    public const int HearthdayDayOfSeason = 8;

    /// <summary>Day of year, 1…32 (Spring 1 = 1).</summary>
    public int DayOfYear => ((int)Season * DaysPerSeason) + Day;

    public bool IsHearthday => Day == HearthdayDayOfSeason;

    /// <summary>Quarter day / court day: day 1 of each season.</summary>
    public bool IsQuarterDay => Day == 1;

    public bool IsMarketDay => Day is 4 or 8;

    public static GameDate FromGameMinute(long gameMinute)
    {
        if (gameMinute < 0) { throw new ArgumentOutOfRangeException(nameof(gameMinute)); }
        var totalDays = gameMinute / (24 * 60);
        var minuteOfDay = (int)(gameMinute % (24 * 60));
        var year = (int)(totalDays / DaysPerYear);
        var dayInYear = (int)(totalDays % DaysPerYear);
        return new GameDate(year, (Season)(dayInYear / DaysPerSeason), (dayInYear % DaysPerSeason) + 1,
            minuteOfDay / 60, minuteOfDay % 60);
    }

    public static GameDate FromGameMs(long gameMs) => FromGameMinute(gameMs / SimClock.MsPerGameMinute);

    public long ToGameMinute()
        => (((long)Year * DaysPerYear) + ((int)Season * DaysPerSeason) + (Day - 1)) * 24 * 60 + (Hour * 60) + Minute;

    /// <summary>Canonical format, e.g. <c>Y0 Spring 1 06:00</c>.</summary>
    public override string ToString()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Y{Year} {Season} {Day} {Hour:00}:{Minute:00}");
}
