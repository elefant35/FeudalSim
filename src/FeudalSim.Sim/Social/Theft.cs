using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Social;

/// <summary>
/// M1-29 placeholder theft (16 §10, owner example "theft → witnesses → wariness and rumor"): the goods move, every awake
/// person nearby rolls 16 §10.1's detection, those who see it hold a first-hand <c>stole</c> belief (c = id_conf × clarity)
/// and the victim, if they believe it, records <c>stolen_from_me</c> and loses trust. Believers with c ≥ 0.6 are wary
/// (§10.6: trust drops; the dialogue cue says so). Suspicion, accusation and law are M3–M5 (17).
/// </summary>
public static class Theft
{
    public const float ReachM = 2.5f;

    internal static void Commit(SimWorld world, in CommandEnvelope command, Steal c)
    {
        var people = world.People;
        var thief = world.PlayerRow;
        var victim = people.IndexOf(c.From);
        var item = Content.ContentDatabase.HandleOf(world.Content.Items, c.Item, i => i.Id);
        string? problem = thief < 0 ? "no player character" : victim < 0 || victim == thief ? "no such person" : item < 0 ? $"unknown item {c.Item}"
            : c.Qty < 1 || world.Holdings.Goods(c.From, item) < c.Qty ? "they don't have it" : !Escalation.Within(world, thief, victim, ReachM) ? "out of reach" : null;
        if (problem is not null) { world.RejectCommand(command, $"Steal: {problem}."); return; }

        world.Holdings.Take(c.From, people.Ids[thief], item, c.Qty);
        var value = world.Content.Items[item].BaseValueF * c.Qty;
        var predicate = world.Content.ClaimHandle("claim.stole");
        var now = world.Clock.GameMinute;
        var claim = world.Claims.Observe(predicate, people.Ids[thief], c.From, Math.Clamp(value / 24f, 0.25f, 3f), now);
        var stealth = world.Content.SkillHandle("skill.stealth") is var sh and >= 0 ? people.SkillLevels(thief)[sh] : 0;
        var cActor = Math.Clamp(0.6f * stealth / 100f, 0f, 0.85f);
        var range = Range(now % 1440);
        var seen = 0;
        for (var k = 0; k < people.Count; k++)
        {
            if (k == thief) { continue; }
            var d = Distance(world, thief, k);
            if (d > range) { continue; }
            var vDist = d <= 5f ? 1f : MathF.Max(0f, 1f - ((d - 5f) / (range - 5f)));
            var p = vDist * Attention(world, k, thief, victim) * (1f - cActor);   // LOS 1: the M1 camp is open ground
            var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Law, people.Ids[k].Value, Salt.TheftDetect, (ulong)world.Clock.Step));
            if (!rng.Chance(p)) { continue; }
            seen++;
            var familiarity = world.Relationships.Familiarity(people.Ids[k], people.Ids[thief]);
            var conf = Math.Clamp(0.4f + (0.6f * vDist) + (0.3f * familiarity / 100f), 0f, 1f) * 1.0f;   // clarity 1.0: taken from a person
            var b = world.Beliefs.GetOrCreate(people.Ids[k], claim, now);
            if (conf > b.C) { (b.C, b.FirstHandC) = (conf, conf); }
            b.FirstHand = true;
            b.NovSinceMin = now;
            if (conf >= 0.6f) { world.Relationships.TrustEvidence(people.Ids[k], people.Ids[thief], -10f); }   // §10.6 wary
            if (k == victim)
            {
                world.Relationships.ApplyModifier(c.From, people.Ids[thief], "opinion.stole_from_me", Math.Clamp(0.5f + (value / 48f), 0.5f, 2f));
                world.Relationships.TrustEvidence(c.From, people.Ids[thief], -15f);
                world.Memories.Remember(c.From, MemoryKind.Stole, people.Ids[thief], c.From, now, 40, 1f, 30f, -70);
            }
        }

        world.Beliefs.Touch();
        world.Emit(Salience.Notable, people.Ids[thief], new TheftCommitted(people.Ids[thief], c.From, item, c.Qty, seen));
    }

    /// <summary>§10.1 sight range: 40 m by day, 15 m at dusk or by firelight, 8 m in the dark.</summary>
    public static float Range(long minuteOfDay) => minuteOfDay switch { >= 360 and < 1140 => 40f, >= 300 and < 1260 => 15f, _ => 8f };

    /// <summary>§10.1 A_w × (0.7 + 0.06·Perception): asleep 0.05 · working 0.35 · socializing 0.5 · idle 0.6 · watching the actor 1.0.</summary>
    private static float Attention(SimWorld world, int w, int actor, int victim)
    {
        ref readonly var a = ref world.People.Activity[w];
        var watching = w == victim && a.Has(ActivityState.Conversing);   // the person you're talking to is looking at you
        var baseA = a.Has(ActivityState.Asleep) ? 0.05f : watching ? 1f : a.Has(ActivityState.Purposeful) ? 0.35f
            : a.Has(ActivityState.Conversing) || a.Has(ActivityState.Interacting) ? 0.5f : 0.6f;
        return baseA * (0.7f + (0.06f * world.People.Attributes[w].Perception));
    }

    private static float Distance(SimWorld world, int a, int b)
    {
        ref readonly var ta = ref world.People.Transforms[a];
        ref readonly var tb = ref world.People.Transforms[b];
        return MathF.Sqrt(((ta.X - tb.X) * (ta.X - tb.X)) + ((ta.Z - tb.Z) * (ta.Z - tb.Z)));
    }

    /// <summary>§10.6: does <paramref name="holder"/> hold a <c>stole</c> belief (c ≥ 0.6) about <paramref name="subject"/>?</summary>
    public static bool Wary(SimWorld world, EntityId holder, EntityId subject)
    {
        var predicate = world.Content.ClaimHandle("claim.stole");
        foreach (ref readonly var b in world.Beliefs.Span(holder))
        {
            ref readonly var claim = ref world.Claims[b.Claim];
            if (b.C >= 0.6f && claim.Predicate == predicate && claim.Subject == subject.Value) { return true; }
        }

        return false;
    }
}
