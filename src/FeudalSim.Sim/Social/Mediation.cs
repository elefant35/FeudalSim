using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;

namespace FeudalSim.Sim.Social;

/// <summary>
/// 16 §9.4 mediation after the fact (M2-26): when a quarrel that reached an argument (rung ≥ 2) is over, the camp member
/// best placed to settle it — Opinion ≥ 20 toward both, the highest of the lower two — tries once, with
/// <c>P = 0.3 + Leadership/200 + Persuasion/400 + 0.2·authority − 0.2 per Stubborn party</c>. Success: both parties'
/// Anger −30, the quarrel's grievance slots (argued, threatened, struck, beaten) ×0.7 both ways, and <c>reconciled</c> +5
/// (h 8 d). Authority is 17's (none in the camp yet); the player is never picked as an NPC mediator. Priests' +0.15 waits for 14.
/// </summary>
public static class Mediation
{
    private static readonly string[] Grievances = ["opinion.argued_with_me", "opinion.threatened_me", "opinion.struck_me", "opinion.beat_me"];

    public static void AfterQuarrel(SimWorld world, Confrontation conf)
    {
        if (conf.Peak < 2) { return; }
        var people = world.People;
        var a = people.IndexOf(conf.A);
        var b = people.IndexOf(conf.B);
        if (a < 0 || b < 0) { return; }
        var rel = world.Relationships;
        var mediator = -1;
        var best = 20f;
        for (var k = 0; k < people.Count; k++)
        {
            if (k == a || k == b || world.IsPlayer(k) || !world.CanAct(k)) { continue; }
            var standing = MathF.Min(rel.Opinion(people.Ids[k], conf.A), rel.Opinion(people.Ids[k], conf.B));
            if (standing >= best && (mediator < 0 || standing > best)) { (mediator, best) = (k, standing); }
        }

        if (mediator < 0) { return; }
        var p = Chance(world, mediator, a, b);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Social, conf.Id, Salt.Mediation, (ulong)world.Clock.Step));
        var success = rng.Chance(p);
        if (success)
        {
            foreach (var row in (ReadOnlySpan<int>)[a, b]) { people.Emotions[row].Anger = MathF.Max(0f, people.Emotions[row].Anger - 30f); }
            foreach (var g in Grievances)
            {
                rel.ScaleModifier(conf.A, conf.B, g, 0.7f);
                rel.ScaleModifier(conf.B, conf.A, g, 0.7f);
            }

            rel.ApplyModifier(conf.A, conf.B, "opinion.reconciled");
            rel.ApplyModifier(conf.B, conf.A, "opinion.reconciled");
        }

        world.Emit(Salience.Minor, people.Ids[mediator], new QuarrelMediated(conf.Id, people.Ids[mediator], conf.A, conf.B, success));
    }

    /// <summary>16 §9.4 success chance for mediator <paramref name="m"/> between rows <paramref name="a"/> and <paramref name="b"/>.</summary>
    public static float Chance(SimWorld world, int m, int a, int b)
    {
        var people = world.People;
        float Skill(string id) => world.Content.SkillHandle(id) is var h and >= 0 ? people.SkillLevels(m)[h] : 0f;
        var stubborn = world.Content.TraitHandle("trait.stubborn");
        var stubbornParties = (people.Personality[a].HasTrait(stubborn) ? 1 : 0) + (people.Personality[b].HasTrait(stubborn) ? 1 : 0);
        return Math.Clamp(0.3f + (Skill("skill.leadership") / 200f) + (Skill("skill.persuasion") / 400f) - (0.2f * stubbornParties), 0f, 1f);
    }
}
