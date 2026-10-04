using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Social;

/// <summary>
/// 16 §4.15 conversation rapport (content <c>dp.rapport</c>): <c>warm_to_speaker</c> (+step <c>warmed_to_me</c>, while the
/// pair's words budget lasts), <c>stay_neutral</c>, <c>cool_to_speaker</c> (−step), step 1–4 from Familiarity.
/// <code>
/// z = s·L + 0.25·(Warmth − 50)/50 + 0.15·Mood/100 + 0.2·[Social &lt; 40] − Anger(at speaker)/100 − 0.15·[Paranoid] + 0.1·clamp(Op, −50, 50)/50
/// U = { warm: 2z, neutral: 0.8, cool: −2z − 0.5 } → p = softmax(U)
/// </code>
/// The DP's subject carries the conversation's mean L_words ×1000, so the menu can be rebuilt after the conversation closed.
/// </summary>
public sealed class RapportOwner : IDecisionPointOwner
{
    public const string Id = "16.rapport";
    public const string Kind = "relationship.rapport";
    public const string MenuId = "dp.rapport";
    public const int MaxPerConversation = 3;
    public const int EveryTurns = 8;

    public string OwnerId => Id;

    public static long Encode(float lWords) => (long)MathF.Round(Math.Clamp(lWords, -1f, 1f) * 1000f);

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var people = world.People;
        var npc = people.IndexOf(context.Chooser);
        var speaker = people.IndexOf(context.Counterpart);
        if (npc < 0 || speaker < 0) { return [Opt(def, "stay_neutral", 1f, true, 0, world, speaker)]; }

        var warmOk = world.Relationships.WordsBudgetLeft(context.Chooser, context.Counterpart) > 0f;
        var (warm, neutral, cool) = Split(Z(world, npc, speaker, context.Subject / 1000f), warmOk);
        var step = Step(world.Relationships.Familiarity(context.Chooser, context.Counterpart));
        return
        [
            Opt(def, "cool_to_speaker", cool, true, step, world, speaker),
            Opt(def, "stay_neutral", neutral, true, 0, world, speaker),
            Opt(def, "warm_to_speaker", warm, warmOk, step, world, speaker),
        ];
    }

    /// <summary>softmax(2z, 0.8, −2z − 0.5); warm drops out when the words budget is spent.</summary>
    public static (float Warm, float Neutral, float Cool) Split(float z, bool warmOk = true)
    {
        float uw = 2f * z, un = 0.8f, uc = (-2f * z) - 0.5f;
        var max = MathF.Max(uw, MathF.Max(un, uc));
        float ew = warmOk ? SimMath.Exp(uw - max) : 0f, en = SimMath.Exp(un - max), ec = SimMath.Exp(uc - max);
        var sum = ew + en + ec;
        return (ew / sum, en / sum, ec / sum);
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        var step = chosen.Params.Length > 0 ? chosen.Params[0].Value : 0;
        if (chosen.Id == "warm_to_speaker") { world.Relationships.ApplyModifier(dp.Context.Chooser, dp.Context.Counterpart, "opinion.warmed_to_me", step); }
        else if (chosen.Id == "cool_to_speaker") { world.Relationships.ApplyModifier(dp.Context.Chooser, dp.Context.Counterpart, "opinion.cooled_on_me", step); }
    }

    /// <summary>16 §4.15's z for the listener toward the speaker, with L = 0.5·L_words + 0.5·L_skill.</summary>
    public static float Z(SimWorld world, int npc, int speaker, float lWords)
    {
        var people = world.People;
        EntityId me = people.Ids[npc], them = people.Ids[speaker];
        ref readonly var p = ref people.Personality[npc];
        ref readonly var e = ref people.Emotions[npc];
        var l = (0.5f * lWords) + (0.5f * MenuWidth.LSkill(MenuWidth.Persuasion(world, speaker)));
        var s = MenuWidth.Susceptibility(world, npc, them);
        var op = world.Relationships.Opinion(me, them);
        var anger = e.AngerTarget == them ? e.Anger : 0f;
        var paranoid = p.HasTrait(world.Content.TraitHandle("trait.paranoid"));
        return (s * l) + (0.25f * (p.Warmth - 50f) / 50f) + (0.15f * people.Mood[npc].Smoothed / 100f) + (people.Needs[npc].Social < 40f ? 0.2f : 0f)
               - (anger / 100f) - (paranoid ? 0.15f : 0f) + (0.1f * Math.Clamp(op, -50f, 50f) / 50f);
    }

    /// <summary>16 §4.15: F &lt; 15 → 1 · 15–34 → 2 · 35–59 → 3 · ≥ 60 → 4.</summary>
    public static int Step(float familiarity) => familiarity < 15f ? 1 : familiarity < 35f ? 2 : familiarity < 60f ? 3 : 4;

    private static MenuOption Opt(DecisionDef def, string id, float p, bool eligible, int step, SimWorld world, int speaker)
    {
        var o = def.Options.First(x => x.Id == id);
        OptionParam[] ps = step > 0 ? [new("step", step)] : [];
        return new MenuOption(id, o.Family, ps, eligible, p, 0f, o.Stakes, o.FavorsPlayer,
            o.Gloss.Replace("{player}", speaker >= 0 ? world.People.Names[speaker] : "them", StringComparison.Ordinal));
    }
}

/// <summary>
/// 16 §4.14 apology (content <c>dp.apology</c>):
/// <code>
/// P(accept) = clamp(0.40 + 0.005·Op + 0.003·(W − 50) − 0.20·Stubborn − 0.25·Vengeful + 0.10·gift − 0.10·(apologies in 4 d) + G·Margin, 0.05, 0.95)
/// </code>
/// with <c>Margin = 0.15·s·(0.5 + 0.5·Persuasion/100)</c> and <c>G = clamp(L, −0.5, 1)</c> from the classified sincerity
/// (carried ×1000 in the DP's subject). Accepted: the incident modifiers keep 50 % (Stubborn 75 %), Anger −30, the quarrel
/// resets, a <c>made_amends</c> claim for those in earshot. <c>demand_amends</c> needs a material harm (M3–M4).
/// </summary>
public sealed class ApologyOwner : IDecisionPointOwner
{
    public const string Id = "16.apology";
    public const string Kind = "apology.respond";
    public const string MenuId = "dp.apology";

    /// <summary>The grievances an apology softens (16 §4.14 "the incident's modifiers").</summary>
    public static readonly string[] Grievances =
    [
        "opinion.insulted_me", "opinion.insulted_my_kin_or_faith", "opinion.mocked_me", "opinion.rude_to_me", "opinion.argued_with_me",
        "opinion.threatened_me", "opinion.struck_me", "opinion.humiliated_me",
    ];

    public string OwnerId => Id;

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var people = world.People;
        var victim = people.IndexOf(context.Chooser);
        var apologizer = people.IndexOf(context.Counterpart);
        var p = victim < 0 || apologizer < 0 ? 0.5f : PAccept(world, victim, apologizer, context.Subject / 1000f);
        var (holder, actor) = (context.Chooser, context.Counterpart);
        var grave = victim >= 0 && Grievances.Any(g => GrievanceMagnitude(world, holder, actor, g) >= 25f);
        string Gloss(string id) => def.Options.First(o => o.Id == id).Gloss.Replace("{player}", apologizer >= 0 ? people.Names[apologizer] : "them", StringComparison.Ordinal);
        return
        [
            new("accept_apology", "accept", [], true, p, 0f, grave ? Stakes.Medium : Stakes.Low, true, Gloss("accept_apology")),
            new("demand_amends", "counter", [], false, 0f, 0f, Stakes.Low, false, Gloss("demand_amends")),
            new("refuse_apology", "refuse", [], true, 1f - p, 0f, Stakes.Low, false, Gloss("refuse_apology")),
        ];
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        if (chosen.Id != "accept_apology") { return; }
        var people = world.People;
        var victim = people.IndexOf(dp.Context.Chooser);
        var apologizer = people.IndexOf(dp.Context.Counterpart);
        if (victim < 0 || apologizer < 0) { return; }
        var keep = people.Personality[victim].HasTrait(world.Content.TraitHandle("trait.stubborn")) ? 0.75f : 0.5f;
        foreach (var g in Grievances) { world.Relationships.ScaleModifier(dp.Context.Chooser, dp.Context.Counterpart, g, keep); }
        ref var e = ref people.Emotions[victim];
        e.Anger = MathF.Max(0f, e.Anger - 30f);
        if (world.Confrontations.Between(dp.Context.Chooser, dp.Context.Counterpart) is { } quarrel) { world.Confrontations.Remove(quarrel.Id); }
        Rumors.Witness(world, "claim.made_amends", apologizer, victim, 1f, EscalationOwner.Earshot);
    }

    public static float PAccept(SimWorld world, int victim, int apologizer, float lWords)
    {
        var people = world.People;
        EntityId v = people.Ids[victim], a = people.Ids[apologizer];
        ref readonly var p = ref people.Personality[victim];
        var persuasion = MenuWidth.Persuasion(world, apologizer);
        var l = (0.5f * lWords) + (0.5f * MenuWidth.LSkill(persuasion));
        var margin = MenuWidth.Margin(MenuWidth.CDefault, MenuWidth.Susceptibility(world, victim, a), persuasion / 100f);
        var g = Math.Clamp(l, -0.5f, 1f);
        var raw = 0.40f + (0.005f * world.Relationships.Opinion(v, a)) + (0.003f * (p.Warmth - 50f))
                  - (p.HasTrait(world.Content.TraitHandle("trait.stubborn")) ? 0.20f : 0f) - (p.HasTrait(world.Content.TraitHandle("trait.vengeful")) ? 0.25f : 0f)
                  - (0.10f * RecentApologies(world, v, a)) + (g * margin);
        return Math.Clamp(raw, 0.05f, 0.95f);
    }

    /// <summary>Apologies from <paramref name="apologizer"/> the victim remembers from the last 4 game days.</summary>
    public static int RecentApologies(SimWorld world, EntityId victim, EntityId apologizer)
    {
        var now = world.Clock.GameMinute;
        var n = 0;
        foreach (ref readonly var m in world.Memories.Span(victim))
        {
            if (m.Kind == MemoryKind.Apology && m.Actor == apologizer.Value && now - m.TimeMin <= 4 * 1440) { n += Math.Max(1, (int)m.Count); }
        }

        return n;
    }

    private static float GrievanceMagnitude(SimWorld world, EntityId holder, EntityId actor, string modifier)
    {
        if (!world.Relationships.TryGet(holder, actor, out var e)) { return 0f; }
        var h = world.Content.OpinionModifierHandle(modifier);
        foreach (var s in e.Mods) { if (s.Modifier == h) { return MathF.Abs(s.Value); } }
        return 0f;
    }
}
