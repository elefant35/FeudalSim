namespace FeudalSim.Sim;

/// <summary>Per-step read-only facts every system receives.</summary>
public readonly struct StepContext
{
    public StepContext(ulong worldSeed, long step, long gameMs, long dtGameMs, int dtEmbodiedMs)
    {
        WorldSeed = worldSeed;
        Step = step;
        GameMs = gameMs;
        DtGameMs = dtGameMs;
        DtEmbodiedMs = dtEmbodiedMs;
    }

    public ulong WorldSeed { get; }
    public long Step { get; }
    public long GameMs { get; }
    public long DtGameMs { get; }
    public int DtEmbodiedMs { get; }

    public long GameMinute => GameMs / Time.SimClock.MsPerGameMinute;
    public float DtGameHours => DtGameMs / (float)Time.SimClock.MsPerGameHour;
    public float DtEmbodiedSeconds => DtEmbodiedMs / 1000f;
}
