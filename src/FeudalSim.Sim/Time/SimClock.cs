namespace FeudalSim.Sim.Time;

/// <summary>
/// The two sim clocks (20 §5.1): <see cref="Step"/> orders everything; <see cref="GameMs"/> is game
/// time since <c>Y0 Spring 1 00:00</c>. Stored timestamps use whole game-minutes.
/// </summary>
public sealed class SimClock
{
    /// <summary>Embodied (real at 1×) milliseconds per fine step.</summary>
    public const int StepMs = 100;

    /// <summary>Allowed day lengths in real minutes; each gives an integer game-ms per step.</summary>
    public static readonly IReadOnlyList<int> AllowedDayLengths = [20, 24, 25, 30, 32, 36, 40, 45, 48, 50, 60];

    public const int DefaultDayLengthMinutes = 30;
    public const long MsPerGameMinute = 60_000;
    public const long MsPerGameHour = 60 * MsPerGameMinute;
    public const long MsPerGameDay = 24 * MsPerGameHour;

    public SimClock(long startGameMs = 0, int dayLengthMinutes = DefaultDayLengthMinutes)
    {
        if (startGameMs < 0) { throw new ArgumentOutOfRangeException(nameof(startGameMs)); }
        if (!IsAllowedDayLength(dayLengthMinutes)) { throw new ArgumentOutOfRangeException(nameof(dayLengthMinutes)); }
        GameMs = startGameMs;
        DayLengthMinutes = dayLengthMinutes;
    }

    public long Step { get; private set; }
    public long GameMs { get; private set; }
    public int DayLengthMinutes { get; private set; }

    /// <summary>Game minutes per real minute: 1440 / day length (48 at the default day; 57.6 at 25 minutes). Display only.</summary>
    public double Ratio => 1440.0 / DayLengthMinutes;

    /// <summary>Game milliseconds added by one fine step: 144,000 / day length (4,800 at the default day). Always whole.</summary>
    public long GameMsPerStep => StepMs * 1440L / DayLengthMinutes;

    public long GameMinute => GameMs / MsPerGameMinute;

    public static bool IsAllowedDayLength(int minutes) => AllowedDayLengths.Contains(minutes);

    /// <summary>Applies a validated day-length change (only via a logged command).</summary>
    public bool TrySetDayLength(int minutes)
    {
        if (!IsAllowedDayLength(minutes)) { return false; }
        DayLengthMinutes = minutes;
        return true;
    }

    /// <summary>Advances one fine step and returns the game-ms that elapsed.</summary>
    public long AdvanceFineStep()
    {
        var dt = GameMsPerStep;
        Step++;
        GameMs += dt;
        return dt;
    }

    /// <summary>Advances one macro step (Interludes, sleep) by an arbitrary game duration.</summary>
    public long AdvanceMacroStep(long gameMs)
    {
        if (gameMs <= 0) { throw new ArgumentOutOfRangeException(nameof(gameMs)); }
        Step++;
        GameMs += gameMs;
        return gameMs;
    }

    internal void Restore(long step, long gameMs, int dayLengthMinutes)
    {
        Step = step;
        GameMs = gameMs;
        DayLengthMinutes = dayLengthMinutes;
    }
}
