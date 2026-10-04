using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;

namespace FeudalSim.Sim.Health;

/// <summary>
/// 11 §5.2–5.3 treatment and infection (M2-06b). A procedure is a Healing check (12 §6.3) at 13 §8's difficulty; its
/// performance score / 100 is 13's <c>TreatmentResult.Q</c> as q. A critical failure does nothing. Skill gates are 11's
/// (bandage, tourniquet, clean, honey: anyone with wound-dressing know-how ≈ Healing 5; poultice Healing 10; stitch and
/// lance 20; cautery 25; setting a bone 40). Supplies (linen, needle, honey, herbs) are not consumed until inventories
/// exist (M2 items). Infection runs per 6-hour slot: onset p = base × c × susceptibility while a wound is open and Clean,
/// then ΔI = 16 − (2 + 0.8·END + 2·bed rest) − treatment + N(0, 3).
/// </summary>
public static class Treatment
{
    /// <summary>13 §8 procedure difficulties and 11's minimum Healing.</summary>
    public static (float Difficulty, int MinHealing) Spec(Procedure p) => p switch
    {
        Procedure.Bandage => (5f, 5), Procedure.Tourniquet => (5f, 5), Procedure.Clean => (15f, 5), Procedure.Honey => (5f, 5),
        Procedure.Poultice => (20f, 10), Procedure.Stitch => (30f, 20), Procedure.Lance => (20f, 20), Procedure.Cautery => (40f, 25),
        _ => (40f, 40),   // set & splint (surgery)
    };

    /// <summary>Why the procedure can't be done on this wound, or null.</summary>
    public static string? Problem(Procedure p, in Injury i, int healing)
    {
        if (healing < Spec(p).MinHealing) { return $"needs Healing {Spec(p).MinHealing}"; }
        var limb = i.Region is BodyRegion.ArmL or BodyRegion.ArmR or BodyRegion.LegL or BodyRegion.LegR;
        return p switch
        {
            Procedure.Tourniquet when !limb => "a tourniquet needs a limb",
            Procedure.Stitch when i.Type != InjuryType.Cut => "only cuts are stitched",
            Procedure.SetAndSplint when i.Type != InjuryType.Fracture => "only fractures are set",
            Procedure.Lance when i.Infection == InfectionState.Clean => "nothing to lance",
            Procedure.Clean or Procedure.Honey or Procedure.Poultice or Procedure.Stitch when !i.Open => "not an open wound",
            _ => null,
        };
    }

    /// <summary>Applies a procedure of quality <paramref name="q"/> (0–1) to one injury (11 §5.2–5.3 effects).</summary>
    public static void Apply(SimWorld world, EntityId patient, ref Injury i, Procedure p, float q)
    {
        var tier = HealthRules.Tier(i.Severity);
        switch (p)
        {
            case Procedure.Bandage:
                i.BleedRate *= tier <= 1 && !i.Arterial ? 0f : tier == 2 ? 0.4f - (0.3f * q) : 0.7f - (0.4f * q);
                i.Treated |= Health.Treated.Bandaged;
                break;
            case Procedure.Tourniquet:
                (i.BleedRate, i.TourniquetMin) = (0f, world.Clock.GameMinute);
                i.Treated |= Health.Treated.Tourniquet;
                break;
            case Procedure.Stitch:
                if (tier >= 2) { i.BleedRate = 0f; }
                i.Treated |= Health.Treated.Stitched;
                break;
            case Procedure.Cautery:
                (i.BleedRate, i.Infection, i.InfectionSev) = (0f, InfectionState.Clean, 0f);
                i.Treated |= Health.Treated.Cauterized;
                break;
            case Procedure.Clean:
                i.Contamination *= 1f - (0.7f * q);   // boiled water (wine or vinegar ×0.6 when drink exists)
                i.Treated |= Health.Treated.Cleaned;
                break;
            case Procedure.Honey:
                i.Contamination *= 0.5f;
                i.Treated |= Health.Treated.Honey;
                break;
            case Procedure.Poultice:
                i.Contamination *= 0.75f;   // yarrow (plantain 0.8, ramsons 0.85 when the flora is gathered)
                (i.PoulticeQ, i.Treated) = (MathF.Max(i.PoulticeQ, q), (ushort)(i.Treated | Health.Treated.Poultice));
                break;
            case Procedure.SetAndSplint:
                i.Treated |= Health.Treated.Set | Health.Treated.Splinted;
                break;
            case Procedure.Lance:
                i.InfectionSev = MathF.Max(0f, i.InfectionSev - 15f);
                i.Treated |= Health.Treated.Lanced;
                break;
        }

        i.TreatQuality = MathF.Max(i.TreatQuality, q);
        if (p == Procedure.Cautery)
        {
            // Cautery stops the bleed at the price of a burn (sev 20) on the same region.
            var burn = new Injury
            {
                Id = world.Injuries.NextId(), CreatedMin = world.Clock.GameMinute, Severity = 20f, Region = i.Region, Type = InjuryType.Burn,
                Contamination = 0.3f, Flags = Injury.OpenFlag, TourniquetMin = -1,
            };
            world.Injuries.Add(patient, burn);
        }
    }

    /// <summary>11 §5.4 treatment factor: untreated 0.6 · treated 0.6 + 0.6q; stitched heals ×1.3.</summary>
    public static float HealFactor(in Injury i) => (i.Treated == 0 ? 0.6f : 0.6f + (0.6f * i.TreatQuality)) * ((i.Treated & Health.Treated.Stitched) != 0 ? 1.3f : 1f);

    /// <summary>11 §5.3 onset base per 6-hour slot by type (compound fractures 0.12).</summary>
    public static float OnsetBase(in Injury i) => i.Type switch
    {
        InjuryType.Cut => 0.06f, InjuryType.Puncture => 0.10f, InjuryType.Burn => 0.08f, InjuryType.Fracture => 0.12f, _ => 0f,
    };

    /// <summary>11 §5.3 susceptibility (nutrition from Satiety until 11 §6.3's states exist: Underfed 1.2, Starving 2.0).</summary>
    public static float Susceptibility(float satiety, bool elder, float warmth)
        => (satiety <= 0f ? 2f : satiety < 20f ? 1.2f : 1f) * (elder ? 1.3f : 1f) * (warmth < 40f ? 1.2f : 1f);

    /// <summary>11 §5.3 progression per slot without the noise: 16 − (2 + 0.8·END + 2·bedRest) − treatment.</summary>
    public static float Progression(in Injury i, float endurance, bool bedRest, bool nursed)
    {
        var treatment = ((i.Treated & Health.Treated.Poultice) != 0 ? 2f + (4f * i.PoulticeQ) : 0f) + ((i.Treated & Health.Treated.Honey) != 0 ? 2f : 0f) + (nursed ? 2f : 0f);
        return 16f - (2f + (0.8f * endurance) + (bedRest ? 2f : 0f)) - treatment;
    }

    /// <summary>Validates and performs a <see cref="Commands.TreatWound"/> (the healer's Healing check gives q).</summary>
    internal static void Command(SimWorld world, in Commands.CommandEnvelope command, Commands.TreatWound c)
    {
        var people = world.People;
        var healer = people.IndexOf(c.Healer);
        var patient = people.IndexOf(c.Patient);
        var list = patient >= 0 ? world.Injuries.ListOf(c.Patient) : null;
        var at = list?.FindIndex(i => i.Id == c.Injury) ?? -1;
        var skill = world.Content.SkillHandle("skill.healing");
        string? problem = healer < 0 || patient < 0 ? "no such person" : at < 0 ? "no such wound" : c.Procedure > (byte)Procedure.Lance ? "unknown procedure"
            : command.Source == Commands.CommandSource.Player && healer != world.PlayerRow ? "the player can only treat as themselves"
            : !world.CanAct(healer) ? "the healer can't act" : world.IsDead(patient) ? "the patient is dead" : null;
        if (problem is null && healer != patient)
        {
            ref readonly var a = ref people.Transforms[healer];
            ref readonly var b = ref people.Transforms[patient];
            if (((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)) > 2.5f * 2.5f) { problem = "out of reach"; }
        }

        var proc = (Procedure)c.Procedure;
        var healing = skill >= 0 && healer >= 0 ? people.SkillLevels(healer)[skill] : 0;
        if (problem is null) { problem = Problem(proc, list![at], healing); }
        if (problem is not null) { world.RejectCommand(command, $"TreatWound: {problem}."); return; }

        var (difficulty, _) = Spec(proc);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, c.Healer.Value, c.Injury, ((ulong)world.Clock.Step << 8) | Salt.Treatment));
        var check = Skills.Skills.Resolve(world, new Skills.CheckRequest(healer, skill, difficulty, HasLight: true), ref rng);
        var q = check.PerformanceScore / 100f;
        var applied = check.Outcome != Skills.Outcome.CritFail;
        var wound = list![at];
        if (applied) { Apply(world, c.Patient, ref wound, proc, q); }
        list[at] = wound;
        Skills.Skills.AwardXp(world, healer, skill, 10f, difficulty, check.Outcome, 0.1f);   // 12: Treatment 10 XP per patient-hour of work
        world.Emit(Salience.Notable, c.Healer, new WoundTreated(c.Healer, c.Patient, c.Injury, c.Procedure, applied ? q : 0f, applied));
    }

    public static InfectionState StateFor(float severity) => severity > 70f ? InfectionState.Septic : severity > 30f ? InfectionState.Infected : InfectionState.Inflamed;

    /// <summary>One 6-hour infection slot for one wound (onset if Clean and open, else progression).</summary>
    public static void Slot(SimWorld world, EntityId person, ref Injury i, long slot, float susceptibility, float endurance, bool bedRest)
    {
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, person.Value, i.Id, ((ulong)slot << 8) | Salt.Infection));
        if (i.Infection == InfectionState.Clean)
        {
            if (!i.Open || !rng.Chance(OnsetBase(i) * i.Contamination * susceptibility)) { return; }
            (i.Infection, i.InfectionSev) = (InfectionState.Inflamed, 0f);
            world.Emit(Salience.Notable, person, new WoundInfected(person, i.Id));
            return;
        }

        i.InfectionSev = Math.Clamp(i.InfectionSev + Progression(i, endurance, bedRest, nursed: false) + rng.Normal(0f, 3f), -1f, 100f);
        i.Infection = i.InfectionSev <= 0f ? InfectionState.Clean : StateFor(i.InfectionSev);
        if (i.Infection == InfectionState.Clean) { i.InfectionSev = 0f; }
    }
}
