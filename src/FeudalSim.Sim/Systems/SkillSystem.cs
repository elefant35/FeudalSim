namespace FeudalSim.Sim.Systems;

/// <summary>12 §5.6: once a game day, rust accrues on skills left unpractised past their grace (every person, every LOD).</summary>
public sealed class SkillSystem : ISimSystem
{
    private long _lastDay = -1;

    public string Name => "Skills";

    public SimPhase Phase => SimPhase.Decide;

    public void Run(in StepContext ctx, SimWorld world)
    {
        var day = ctx.GameMinute / 1440;
        if (day == _lastDay) { return; }
        _lastDay = day;
        for (var i = 0; i < world.People.Count; i++) { if (!world.IsDead(i)) { Skills.Skills.DailyRust(world, i); } }
    }
}
