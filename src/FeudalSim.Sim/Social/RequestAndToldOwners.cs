using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Social;

/// <summary>
/// 16 §5.4 request DP (content <c>dp.request</c>). M1 favors: work at a camp task for some hours.
/// <code>
/// A = 0.25 + 0.006·Op + 0.003·T + 0.004·(W − 50) + 0.10·Charitable + reciprocity − 0.004·hours·(100 − Diligence)/50 + Fear/500   (kin: 16 §11)
/// p_accept = Yes(A, Margin, L) · 0.5^(n−1);  accept_with_condition (1 − p)·0.35 (hours ≥ 1);  defer (1 − p)·0.15;  refuse the rest
/// </code>
/// The DP subject packs the task, the hours ×4 and L_words ×1000 so the menu can be rebuilt at commit.
/// </summary>
public sealed class RequestOwner : IDecisionPointOwner
{
    public const string Id = "16.request";
    public const string Kind = "request.respond";
    public const string MenuId = "dp.request";

    public string OwnerId => Id;

    public static long Pack(short task, float hours, float lWords)
        => ((long)Math.Clamp((int)MathF.Round(hours * 4f), 0, 255) << 40) | ((long)(task + 1) << 20) | (long)(RapportOwner.Encode(lWords) + 1000);

    public static (short Task, float Hours, float LWords) Unpack(long subject)
        => ((short)(((subject >> 20) & 0xFFFFF) - 1), ((subject >> 40) & 0xFF) / 4f, ((subject & 0xFFFFF) - 1000) / 1000f);

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var people = world.People;
        var helper = people.IndexOf(context.Chooser);
        var asker = people.IndexOf(context.Counterpart);
        var (task, hours, lWords) = Unpack(context.Subject);
        var feasible = task >= 0 && task < world.Content.Actions.Count && Meets(world, world.Content.Actions[task]);
        var p = helper < 0 || asker < 0 || !feasible ? 0f : PAccept(world, helper, asker, hours, lWords);
        var cond = feasible && hours >= 1f ? (1f - p) * 0.35f : 0f;
        var defer = feasible ? (1f - p) * 0.15f : 0f;
        OptionParam[] ps = [new("hours_x4", (long)MathF.Round(hours * 4f)), new("task", task)];
        string Gloss(string id)
        {
            var o = def.Options.First(x => x.Id == id);
            var taskName = task >= 0 && task < world.Content.Actions.Count ? world.Content.Actions[task].Name.ToLowerInvariant() : "it";
            return o.Gloss.Replace("{player}", asker >= 0 ? people.Names[asker] : "them", StringComparison.Ordinal)
                .Replace("{task}", taskName, StringComparison.Ordinal).Replace("{hours}", hours.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return
        [
            new("accept_request", "accept", ps, feasible, p, 0f, hours > 2f ? Stakes.Medium : Stakes.Low, true, Gloss("accept_request")),
            new("accept_with_condition", "accept", ps, cond > 0f, cond, 0f, hours > 2f ? Stakes.Medium : Stakes.Low, true, Gloss("accept_with_condition")),
            new("defer", "counter", ps, defer > 0f, defer, 0f, Stakes.Low, false, Gloss("defer")),
            new("refuse_request", "refuse", ps, true, 1f - p - cond - defer, 0f, Stakes.Low, false, Gloss("refuse_request")),
        ];
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        var people = world.People;
        var helper = people.IndexOf(dp.Context.Chooser);
        var asker = people.IndexOf(dp.Context.Counterpart);
        if (helper < 0 || asker < 0) { return; }
        var (task, hours, _) = Unpack(dp.Context.Subject);
        var now = world.Clock.GameMinute;
        EntityId h = dp.Context.Chooser, a = dp.Context.Counterpart;
        switch (chosen.Id)
        {
            case "accept_request" or "accept_with_condition":
                world.Favors.Add(h, a, task, now, hours * 60f, chosen.Id == "accept_request" ? FavorKind.Asked : FavorKind.Conditional);
                world.Relationships.ApplyModifier(a, h, "opinion.granted_my_request");
                Rumors.Witness(world, "claim.helped", helper, asker, 1f, EscalationOwner.Earshot);
                break;

            case "defer":
                var tomorrow = ((now / 1440) + 1) * 1440;
                world.Favors.Add(h, a, task, tomorrow + (7 * 60) + 30, hours * 60f, FavorKind.Deferred);   // the next work block (Landfall 07:30)
                break;

            case "refuse_request":
                world.Relationships.ApplyModifier(a, h, "opinion.refused_my_request");
                world.Memories.Remember(a, MemoryKind.Refused, h, a, now, 10, 1f, 0f, -30);
                break;
        }

        world.Emit(Salience.Minor, h, new RequestAnswered(h, a, chosen.Id, task, hours));
    }

    public static float PAccept(SimWorld world, int helper, int asker, float hours, float lWords)
    {
        var people = world.People;
        EntityId h = people.Ids[helper], a = people.Ids[asker];
        ref readonly var p = ref people.Personality[helper];
        var rel = world.Relationships;
        var favors = 0;
        foreach (ref readonly var m in world.Memories.Span(h)) { if (m.Kind == MemoryKind.Help && m.Actor == a.Value) { favors += m.Count; } }
        var reciprocity = MathF.Min(0.2f, 0.05f * favors);
        var baseA = 0.25f + (0.006f * rel.Opinion(h, a)) + (0.003f * rel.Trust(h, a)) + (0.004f * (p.Warmth - 50f))
                    + (p.HasTrait(world.Content.TraitHandle("trait.charitable")) ? 0.10f : 0f) + reciprocity
                    - (0.004f * hours * (100f - p.Diligence) / 50f) + (rel.Fear(h, a) / 500f);
        baseA = Math.Clamp(baseA, 0.02f, 0.98f);
        var persuasion = MenuWidth.Persuasion(world, asker);
        var margin = MenuWidth.Margin(MenuWidth.CDefault, MenuWidth.Susceptibility(world, helper, a), persuasion / 100f);
        var l = (0.5f * lWords) + (0.5f * MenuWidth.LSkill(persuasion));
        var asks = rel.AsksToday(h, a);
        return MenuWidth.Yes(baseA, margin, l) * MathF.Pow(0.5f, Math.Max(0, asks - 1));
    }

    private static bool Meets(SimWorld world, ActionDef def)
    {
        if (def.Requires is { } req) { foreach (var (stock, min) in req) { if (world.Camp.Stock(stock) < min) { return false; } } }
        return !def.ChosenBySystem;
    }
}

/// <summary>
/// 16 §7.10 being-told DP for claims the player tells in conversation (content <c>dp.told</c>), with 22 §6.3's words term:
/// <c>p_take = clamp(cred + G·Margin, 0.05, 0.95)</c>, split into believe / repeat / keep_quiet by 16's ρ and q. The DP
/// subject packs the claim id, first-hand and L_words.
/// </summary>
public sealed class BeingToldOwner : IDecisionPointOwner
{
    public const string Id = "16.told";
    public const string Kind = "belief.respond";
    public const string MenuId = "dp.told";
    public const float FirstHandCs = 0.9f, HearsayCs = 0.7f;

    public string OwnerId => Id;

    public static long Pack(int claim, bool firstHand, float lWords) => ((long)claim << 21) | (firstHand ? 1L << 20 : 0L) | (long)(RapportOwner.Encode(lWords) + 1000);

    public static (int Claim, bool FirstHand, float LWords) Unpack(long s) => ((int)(s >> 21), ((s >> 20) & 1) == 1, ((s & 0xFFFFF) - 1000) / 1000f);

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var people = world.People;
        var listener = people.IndexOf(context.Chooser);
        var teller = people.IndexOf(context.Counterpart);
        var (claim, firstHand, lWords) = Unpack(context.Subject);
        var cS = firstHand ? FirstHandCs : HearsayCs;
        var masses = listener < 0 || teller < 0 || claim < 0 || claim >= world.Claims.Count
            ? new ToldMasses(1f, 0f, 0f, 0f)
            : Masses(world, listener, teller, claim, cS, lWords);
        var text = claim >= 0 && claim < world.Claims.Count ? ClaimText(world, claim) : "something";
        string Gloss(string id) => def.Options.First(o => o.Id == id).Gloss
            .Replace("{player}", teller >= 0 ? people.Names[teller] : "them", StringComparison.Ordinal).Replace("{claim}", text, StringComparison.Ordinal);
        var stakes = claim >= 0 && claim < world.Claims.Count && world.Content.ClaimPredicates[world.Claims[claim].Predicate].Accusation ? Stakes.Medium : Stakes.Low;
        OptionParam[] ps = [new("claim", claim)];
        return
        [
            new("believe", "believe", ps, true, masses.Believe, 0f, stakes, true, Gloss("believe")),
            new("doubt", "doubt", ps, true, masses.Doubt, 0f, Stakes.Low, false, Gloss("doubt")),
            new("keep_quiet", "believe", ps, masses.KeepQuiet > 0f, masses.KeepQuiet, 0f, stakes, false, Gloss("keep_quiet")),
            new("repeat", "believe", ps, masses.Repeat > 0f, masses.Repeat, 0f, stakes, true, Gloss("repeat")),
        ];
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        var people = world.People;
        var listener = people.IndexOf(dp.Context.Chooser);
        var teller = people.IndexOf(dp.Context.Counterpart);
        var (claim, firstHand, _) = Unpack(dp.Context.Subject);
        if (listener < 0 || teller < 0 || claim < 0 || claim >= world.Claims.Count) { return; }
        var option = chosen.Id switch { "believe" => ToldOption.Believe, "repeat" => ToldOption.Repeat, "keep_quiet" => ToldOption.KeepQuiet, _ => ToldOption.Doubt };
        Rumors.ApplyTold(world, listener, teller, claim, firstHand ? FirstHandCs : HearsayCs, 1, option);
        ref readonly var c = ref world.Claims[claim];
        var j = world.Content.ClaimPredicates[c.Predicate].Juiciness;
        world.Memories.Remember(dp.Context.Chooser, MemoryKind.Told, dp.Context.Counterpart, new EntityId(c.Subject), world.Clock.GameMinute, (int)(8 + (40 * j)), 1f, 0f, 0);
        world.Emit(Salience.Trace, dp.Context.Counterpart, new GossipExchanged(dp.Context.Counterpart, dp.Context.Chooser, claim, world.Claims.Root(claim), false, option));
    }

    public static ToldMasses Masses(SimWorld world, int listener, int teller, int claim, float cS, float lWords)
    {
        var persuasion = MenuWidth.Persuasion(world, teller);
        var margin = MenuWidth.Margin(MenuWidth.CDefault, MenuWidth.Susceptibility(world, listener, world.People.Ids[teller]), persuasion / 100f);
        var g = Math.Clamp((0.5f * lWords) + (0.5f * MenuWidth.LSkill(persuasion)), -0.5f, 1f);
        return Rumors.HearMasses(world, listener, teller, claim, cS, 1, g, margin);
    }

    /// <summary>
    /// 16 §5.2 lie test: the teller asserts a false claim they don't hold (c &lt; 0.6). Detected with
    /// <c>clamp(0.1 + 0.02·Perception + F/400 + 0.5·[contradicts the listener's belief ≥ 0.6] + s_consistency, 0, 0.9)</c>
    /// (s_consistency from the fast decider: M1-11; 0 here). Keyed draw.
    /// </summary>
    public static (bool Lie, bool Detected) LieTest(SimWorld world, int listener, int teller, int claim)
    {
        ref readonly var c = ref world.Claims[claim];
        var people = world.People;
        var held = world.Beliefs.Get(people.Ids[teller], claim)?.C ?? 0f;
        if (c.True == 1 || held >= 0.6f) { return (false, false); }
        var contradicts = false;
        foreach (var b in world.Beliefs.Span(people.Ids[listener]))
        {
            ref readonly var o = ref world.Claims[b.Claim];
            if (b.C >= 0.6f && o.Predicate == c.Predicate && o.Object == c.Object && o.Subject != c.Subject) { contradicts = true; break; }
        }

        var f = world.Relationships.Familiarity(people.Ids[listener], people.Ids[teller]);
        var p = Math.Clamp(0.1f + (0.02f * people.Attributes[listener].Perception) + (f / 400f) + (contradicts ? 0.5f : 0f), 0f, 0.9f);
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Social, people.Ids[listener].Value, (ulong)claim, Salt.LieTest));
        return (true, rng.Chance(p));
    }

    private static string ClaimText(SimWorld world, int claim)
    {
        ref readonly var c = ref world.Claims[claim];
        var phrase = world.Content.ClaimPredicates[c.Predicate].Phrase;
        string Name(ulong id) => world.People.IndexOf(new EntityId(id)) is var r and >= 0 ? world.People.Names[r] : "someone";
        return phrase.Replace("{subject}", Name(c.Subject), StringComparison.Ordinal).Replace("{object}", Name(c.Object), StringComparison.Ordinal);
    }
}
