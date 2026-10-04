using FeudalSim.Sim;

namespace FeudalSim.Hosting;

/// <summary>What the view needs each step (20 §2.4). M0: clock and positions; grows with the client.</summary>
public sealed class RenderSnapshot
{
    public long Step;
    public long GameMs;
    public int Count;
    public ulong[] Ids = new ulong[64];
    public float[] X = new float[64];
    public float[] Z = new float[64];
    public float[] Yaw = new float[64];
    public byte[] Tier = new byte[64];
    public float[] TargetX = new float[64];
    public float[] TargetZ = new float[64];
    public bool[] HasTarget = new bool[64];

    /// <summary>Current action handle per settler (−1 none); M1 camp view colours by it.</summary>
    public short[] Action = new short[64];

    /// <summary>The player's own character (not drawn as a settler); activity flags (conversing, deliberating…).</summary>
    public bool[] IsPlayer = new bool[64];
    public byte[] ActivityFlags = new byte[64];

    /// <summary>11 §14 state per person (<see cref="Sim.Health.VitalState"/> as a byte): the client lays down the downed and the dead.</summary>
    public byte[] Vital = new byte[64];

    /// <summary>Camp stocks (M1 graybox camp): food in Satiety points, firewood bundles, minutes of fire left.</summary>
    public float Food, Firewood, FireFuelMin;
    public bool CampActive;

    /// <summary>The region's weather (10 §6.3; M2-03) for the sky, light and HUD.</summary>
    public Sim.Climate.WeatherState Weather;

    /// <summary>The player's body (M2-05): stamina, its max and Winded, warmth and wetness, for the HUD and the client's gait.</summary>
    public float PlayerStamina, PlayerStaminaMax, PlayerWarmth, PlayerWetness;

    public bool PlayerWinded;

    /// <summary>The player's 11 §14 state and §4.3 move-speed multiplier (a downed player crawls; a dead one doesn't move).</summary>
    public byte PlayerVital;

    public float PlayerMoveMult = 1f, PlayerHealth = 100f, PlayerBlood = 100f;

    /// <summary>The player's person id (none if no player).</summary>
    public Sim.Core.EntityId PlayerId;

    /// <summary>The world seed (the client plays seeded minigames with the sim's own code, 13 §7.1).</summary>
    public ulong WorldSeed;

    /// <summary>The player's open process (13 §4; 0 if none): its stage, state, when its labor ends, and the stage's feel (13 §7.2).</summary>
    public ulong PlayerProcess;

    public int ProcessRecipe = -1, ProcessStage;
    public byte ProcessState;
    public long ProcessBusyUntilMin;
    public float StageGrip = 0.5f, StageDex = 5f;

    public void CopyFrom(SimWorld world)
    {
        Step = world.Clock.Step;
        GameMs = world.Clock.GameMs;
        var p = world.People;
        if (Ids.Length < p.Count)
        {
            var size = Math.Max(p.Count, Ids.Length * 2);
            Ids = new ulong[size]; X = new float[size]; Z = new float[size]; Yaw = new float[size];
            Tier = new byte[size]; TargetX = new float[size]; TargetZ = new float[size]; HasTarget = new bool[size];
            Action = new short[size];
            IsPlayer = new bool[size];
            ActivityFlags = new byte[size];
            Vital = new byte[size];
        }

        Count = p.Count;
        for (var i = 0; i < p.Count; i++)
        {
            Ids[i] = p.Ids[i].Value;
            X[i] = p.Transforms[i].X;
            Z[i] = p.Transforms[i].Z;
            Yaw[i] = p.Transforms[i].Yaw;
            Tier[i] = (byte)p.Lod[i].Tier;
            TargetX[i] = p.Wander[i].TargetX;
            TargetZ[i] = p.Wander[i].TargetZ;
            HasTarget[i] = p.Wander[i].HasTarget;
            Action[i] = p.Activity[i].Action;
            IsPlayer[i] = world.IsPlayer(i);
            ActivityFlags[i] = p.Activity[i].Flags;
            Vital[i] = (byte)p.Vitals[i].State;
        }

        CampActive = world.Camp.Active != 0;
        Weather = world.Weather;
        WorldSeed = world.WorldSeed;
        PlayerId = world.PlayerId;
        if (world.PlayerRow is var pr and >= 0)
        {
            ref readonly var n = ref p.Needs[pr];
            ref readonly var st = ref p.Stamina[pr];
            var athletics = world.Content.SkillHandle("skill.athletics");
            (PlayerStamina, PlayerWarmth, PlayerWetness, PlayerWinded) = (st.Value, n.Warmth, p.Body[pr].Wetness, world.Clock.Step < st.WindedUntilStep);
            ref readonly var v = ref p.Vitals[pr];
            (PlayerVital, PlayerHealth, PlayerBlood) = ((byte)v.State, v.Health, v.Blood);
            PlayerMoveMult = Sim.Health.HealthRules.MoveSpeedMult(world.Injuries.Of(p.Ids[pr]), v.Blood);
            PlayerProcess = 0;
            foreach (var proc in world.Processes.Open)
            {
                if (proc.Worker != p.Ids[pr]) { continue; }
                var recipe = world.Content.Recipes[proc.Recipe];
                (PlayerProcess, ProcessRecipe, ProcessStage, ProcessState, ProcessBusyUntilMin) = (proc.Id, proc.Recipe, proc.Stage, (byte)proc.State, proc.BusyUntilMin);
                var skill = world.Content.SkillHandle(recipe.Skill);
                var stageD = recipe.Difficulty + (proc.Stage < recipe.Stages.Count ? recipe.Stages[proc.Stage].DOffset : 0) + (proc.Masterwork ? 15 : 0);
                var (_, tier, toolQ) = recipe.Tools.Count == 0 ? (0UL, Sim.Skills.ToolTier.Iron, 50) : Sim.Crafting.Processes.BestTool(world, proc.Worker, recipe.Tools[0].Tag);
                var e = Sim.Skills.Skills.Effective(world, new Sim.Skills.CheckRequest(pr, skill, stageD, tier == Sim.Skills.ToolTier.None ? Sim.Skills.ToolTier.Iron : tier, Math.Max(0, toolQ), HasLight: true));
                var feel = Sim.Crafting.Minigames.Feel.For(e, stageD, Sim.Skills.Skills.Attribute(world, pr, "dex"));
                (StageGrip, StageDex) = (feel.Grip, feel.Dex);
                break;
            }
            PlayerStaminaMax = Sim.Survival.StaminaRules.Max(Sim.Skills.Skills.Attribute(world, pr, "end"), athletics >= 0 ? p.SkillLevels(pr)[athletics] : 0f, n.Energy, n.Satiety);
        }
        (Food, Firewood, FireFuelMin) = (world.Camp.Food, world.Camp.Firewood, world.Camp.FireFuelMin);
    }
}
