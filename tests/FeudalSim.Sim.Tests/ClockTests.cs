using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Time;

namespace FeudalSim.Sim.Tests;

public class ClockTests
{
    [Fact]
    public void Eighteen_thousand_steps_make_one_game_day_at_30_minute_days()
    {
        var clock = new SimClock();
        clock.GameMsPerStep.ShouldBe(4_800);
        for (var i = 0; i < 18_000; i++) { clock.AdvanceFineStep(); }
        clock.GameMs.ShouldBe(SimClock.MsPerGameDay);
        clock.Step.ShouldBe(18_000);
    }

    [Fact]
    public void Every_allowed_day_length_gives_whole_game_ms_per_step()
    {
        foreach (var minutes in SimClock.AllowedDayLengths)
        {
            (144_000 % minutes).ShouldBe(0, $"{minutes}-minute day");
            var clock = new SimClock(0, minutes);
            (clock.GameMsPerStep * (minutes * 600)).ShouldBe(SimClock.MsPerGameDay);   // steps per real day-length
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(31)]
    [InlineData(61)]
    public void Invalid_day_lengths_are_rejected(int minutes)
    {
        new SimClock().TrySetDayLength(minutes).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => new SimClock(0, minutes));
    }

    [Fact]
    public void Invalid_day_length_command_is_rejected_with_an_event()
    {
        var world = new SimWorld(1);
        world.Enqueue(new CommandEnvelope(1, 0, CommandSource.Settings, new SetDayLength(31)));
        var output = world.Step();
        output.Events.ShouldContain(e => e.Payload is CommandRejected);
        world.Clock.DayLengthMinutes.ShouldBe(30);

        world.Enqueue(new CommandEnvelope(2, 0, CommandSource.Settings, new SetDayLength(60)));
        world.Step().Events.ShouldContain(e => e.Payload is DayLengthChanged);
        world.Clock.GameMsPerStep.ShouldBe(2_400);
    }
}
