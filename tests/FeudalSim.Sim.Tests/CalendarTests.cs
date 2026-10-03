using FeudalSim.Sim.Time;

namespace FeudalSim.Sim.Tests;

public class CalendarTests
{
    [Fact]
    public void Origin_is_Y0_Spring_1_midnight() => GameDate.FromGameMinute(0).ToString().ShouldBe("Y0 Spring 1 00:00");

    [Fact]
    public void Landfall_formats_canonically()
        => GameDate.FromGameMinute((5 * 60) + 30).ToString().ShouldBe("Y0 Spring 1 05:30");

    [Theory]
    [InlineData(0, "Y0 Spring 1 06:00")]
    [InlineData(7, "Y0 Spring 8 06:00")]
    [InlineData(8, "Y0 Summer 1 06:00")]
    [InlineData(16, "Y0 Autumn 1 06:00")]
    [InlineData(24, "Y0 Winter 1 06:00")]
    [InlineData(31, "Y0 Winter 8 06:00")]
    [InlineData(32, "Y1 Spring 1 06:00")]
    public void Eight_day_seasons_and_32_day_years(int days, string expected)
        => GameDate.FromGameMinute((days * 24 * 60) + (6 * 60)).ToString().ShouldBe(expected);

    [Fact]
    public void Hearthday_is_day_8_and_market_days_are_4_and_8()
    {
        for (var day = 0; day < GameDate.DaysPerYear; day++)
        {
            var d = GameDate.FromGameMinute(day * 24L * 60);
            d.IsHearthday.ShouldBe(d.Day == 8);
            d.IsMarketDay.ShouldBe(d.Day is 4 or 8);
            d.IsQuarterDay.ShouldBe(d.Day == 1);
        }
    }

    [Fact]
    public void Day_of_year_runs_1_to_32()
    {
        GameDate.FromGameMinute(0).DayOfYear.ShouldBe(1);
        GameDate.FromGameMinute(12 * 24 * 60).DayOfYear.ShouldBe(13);   // Summer 5: grain harvest opens (canon §6)
        GameDate.FromGameMinute(31 * 24 * 60).DayOfYear.ShouldBe(32);
    }

    [Fact]
    public void Round_trips_through_game_minutes()
    {
        foreach (var minute in new long[] { 0, 1, 1439, 1440, 46_079, 46_080, 9_999_999 })
        {
            GameDate.FromGameMinute(minute).ToGameMinute().ShouldBe(minute);
        }
    }
}
