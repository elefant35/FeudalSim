using FeudalSim.Sim.Climate;

namespace FeudalSim.Sim.Tests;

/// <summary>M2-03: 10 §6.1–6.4 calendar, temperature and the weather chain.</summary>
public sealed class WeatherTests
{
    [Theory]
    [InlineData(1, 10.5)]    // Spring 1 (Landfall)
    [InlineData(3, 12.0)]    // equinox
    [InlineData(11, 16.0)]   // Summer 3
    [InlineData(27, 8.0)]    // Winter 3
    public void Daylight_matches_the_table(int day, double hours) => Weather.DaylightHours(day).ShouldBe((float)hours, 0.25f);

    [Fact]
    public void Landfall_sunrise_is_0645()
    {
        Weather.IsDaylight((6 * 60) + 40).ShouldBeFalse();
        Weather.IsDaylight((6 * 60) + 50).ShouldBeTrue();
        Weather.IsDaylight((17 * 60) + 20).ShouldBeFalse();
    }

    [Fact]
    public void Moon_is_new_on_day_1_and_full_on_day_5()
    {
        Weather.MoonPhase(0).ShouldBe(0f);
        Weather.MoonPhase(4 * 1440).ShouldBe(0.5f);
    }

    [Theory]
    [InlineData(1, 2.5)]
    [InlineData(4, 6.8)]
    [InlineData(9, 14.5)]
    [InlineData(28, 0.2)]
    public void Mean_temperature_matches_the_table(int day, double c) => Weather.MeanTempC(day).ShouldBe((float)c, 0.1f);

    [Fact]
    public void Worked_example_landfall_predawn_is_minus_two()
    {
        var w = new WeatherState { Sky = Sky.Clear, Anomaly = -1f };
        Weather.AirTempC(w, 4 * 60, elevationM: 3f, nearCoast: true).ShouldBe(-2.0f, 0.1f);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Stationary_shares_are_within_three_points_of_the_table(int regime)
    {
        var target = Weather.RegimeTarget(regime);
        var shares = Weather.StationaryShares(regime);
        for (var k = 0; k < 6; k++) { Math.Abs(shares[k] - target[k]).ShouldBeLessThan(0.03, $"regime {regime} state {(Sky)k}"); }
    }

    [Fact]
    public void A_long_run_matches_the_spring_table_and_obeys_the_adjacency_rules()
    {
        var counts = new int[6];
        var w = default(WeatherState);
        var prev = Sky.Clear;
        var n = 0;
        for (long slot = 0; slot < 200_000; slot++)
        {
            Weather.Advance(ref w, 42, slot);
            var d = Weather.DayOfYear(slot * Weather.SlotMinutes);
            if (slot > 0 && w.Sky == Sky.Storm && prev != Sky.Storm) { prev.ShouldBeOneOf(Sky.Cloudy, Sky.Drizzle, Sky.Rain); }
            if (slot > 0 && w.Sky == Sky.Fog && prev != Sky.Fog) { (slot % 4).ShouldBeLessThan(2); }
            if (d <= 8) { counts[(int)w.Sky]++; n++; }
            prev = w.Sky;
        }

        var target = Weather.RegimeTarget(0);
        for (var k = 0; k < 6; k++) { Math.Abs((counts[k] / (double)n) - target[k]).ShouldBeLessThan(0.03, ((Sky)k).ToString()); }
    }

    [Fact]
    public void The_chain_is_keyed_per_slot_so_catching_up_equals_stepping()
    {
        var a = default(WeatherState);
        var b = default(WeatherState);
        for (long s = 0; s < 500; s++) { Weather.Advance(ref a, 7, s); }
        for (long s = 0; s < 500; s++) { Weather.Advance(ref b, 7, s); }
        a.ShouldBe(b);
        var c = default(WeatherState);
        Weather.Advance(ref c, 8, 0);
        for (long s = 1; s < 500; s++) { Weather.Advance(ref c, 8, s); }
        c.Equals(a).ShouldBeFalse();
    }

    [Fact]
    public void Year_zero_winter_is_never_hard()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var w = default(WeatherState);
            Weather.Advance(ref w, seed, 0);
            w.WinterType.ShouldNotBe((byte)2);
        }
    }
}
