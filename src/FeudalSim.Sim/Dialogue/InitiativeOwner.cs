using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Systems;

namespace FeudalSim.Sim.Dialogue;

/// <summary>
/// The NPC's initiative DP, opened every NPC turn beside its answer (21 §14.5): at most 3 initiative options, plus
/// <c>none</c> and <c>end_conversation</c>. Candidates come from the NPC's state (sources available in M1 below), are
/// weighted <c>B(o) = W(o)/θ_init</c> (θ_init = 2.0) and the §7.8 character terms, the top 3 kept; <c>none</c> has B = 1
/// (2 when the player just asked or requested something: answer first). <c>end_conversation</c> takes
/// <c>p_end = logistic(10·(best alternative / score(converse) − 1.3))</c> from the utility scorer (§14.4) and the rest
/// share <c>1 − p_end</c>. Pacing: an option acted on is not offered for the next 3 NPC turns; one the player declined is
/// re-offered at most once, at 0.5× (canon §13.4).
/// <para>Sources in M1: <b>ask_favor</b> (was working, tired: W = 0.5 + 2·(100 − Energy)/100); <b>challenge</b> (Anger at
/// the player ≥ 50: W = 1 + 4·(Anger − 50)/50, or an insult with Honor ≥ 60: 1.5); <b>accuse</b> (a held negative claim about
/// the player, c ≥ 0.7, not yet said to them: W = 1 + 2·c·J); <b>warn</b> (a held negative claim about something done to the
/// player, c ≥ 0.5, Opinion ≥ 20: W = 1.5); <b>share_gossip</b> (a tellable claim and the Gossip trait or Sociability ≥ 65:
/// W = 1 + Tell); <b>invite</b> (Social &lt; 60, between noon and the evening gathering: W = 0.5 + 2·u(social)).</para>
/// </summary>
public sealed class InitiativeOwner : IDecisionPointOwner
{
    public const string Id = "21.initiative";
    public const string Kind = "conv.initiative";
    public const string MenuId = "dp.conv_initiative";
    public const float ThetaInit = 2.0f;
    public const int MaxInitiatives = 3;
    public const int RestTurns = 3;

    private static readonly HashSet<string> AskedActs = new(StringComparer.Ordinal) { "ask", "why_did_you", "request", "trade_offer", "accept_offer", "reject_offer" };
    private static readonly IReadOnlySet<string> AllTerms = new HashSet<string>(StringComparer.Ordinal) { "personality", "emotion", "need", "opinion", "schedule" };

    public string OwnerId => Id;

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var def = world.Content.Decision(MenuId) ?? throw new InvalidOperationException($"Content has no {MenuId}.");
        var people = world.People;
        var npc = people.IndexOf(context.Chooser);
        var player = people.IndexOf(context.Counterpart);
        var conv = world.Conversations.Get((ulong)context.Subject);
        if (npc < 0 || player < 0 || conv is null)
        {
            return [Option(def, "none", [], 1f, world, player), Option(def, "end_conversation", [new("reason", 0)], 0f, world, player)];
        }

        // Candidates with their source strength W and fixed parameters.
        var candidates = new List<(string Id, float W, OptionParam[] Params)>();
        Sources(world, npc, player, conv, candidates);

        // Pacing (21 §14.5).
        for (var k = candidates.Count - 1; k >= 0; k--)
        {
            var id = candidates[k].Id;
            var declined = conv.Declined.GetValueOrDefault(id);
            if (conv.LastActed.TryGetValue(id, out var turn) && conv.Turn - turn <= RestTurns) { candidates.RemoveAt(k); continue; }
            if (declined >= 2) { candidates.RemoveAt(k); continue; }
            if (declined == 1) { candidates[k] = candidates[k] with { W = candidates[k].W * 0.5f }; }
        }

        var noneMass = AskedActs.Contains(conv.LastAct) ? 2f : 1f;
        var agenda = conv.Agenda.Length > 0 ? 2.5f : 1f;   // P2 agenda (an overdue debt, a summons), NPC-initiated only
        var top = TopByPropensity(world, npc, player, def, candidates, noneMass, agenda);

        var pEnd = PEnd(world, npc);
        var options = new List<MenuOption>(top.Count + 2);
        var (p, _) = Propensities(world, npc, player, def, top, noneMass, agenda);
        for (var k = 0; k < top.Count; k++) { options.Add(Option(def, top[k].Id, top[k].Params, p[k + 1] * (1f - pEnd), world, player)); }
        options.Add(Option(def, "none", [], p[0] * (1f - pEnd), world, player));
        options.Add(Option(def, "end_conversation", [new("reason", EndReason(world, npc))], pEnd, world, player));
        return options;
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        if (world.Conversations.Get((ulong)dp.Context.Subject) is not { } conv) { return; }
        var npc = world.People.IndexOf(conv.Npc);
        var player = world.People.IndexOf(conv.Player);
        if (chosen.Id == "none") { return; }
        if (chosen.Id == "end_conversation")
        {
            ConversationSystem.End(world, conv, "npc");
            return;
        }

        conv.LastActed[chosen.Id] = conv.Turn;
        var claim = Param(chosen, "claim");
        switch (chosen.Id)
        {
            case "ask_favor" or "challenge" or "invite":
                conv.PendingOffer = chosen.Id;   // the player answers in their own turn (21 §16)
                break;

            case "share_gossip" when claim >= 0 && npc >= 0 && player >= 0:
                if (world.Beliefs.Get(conv.Npc, (int)claim) is { } told)
                {
                    var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Rumor, conv.Npc.Value, dp.Id, Salt.Initiative));
                    Rumors.Exchange(world, npc, player, told, ref rng);
                }

                break;

            case "warn" or "accuse" when claim >= 0 && npc >= 0 && player >= 0:
                if (world.Beliefs.Get(conv.Npc, (int)claim) is { } belief)
                {
                    // The player learns what is believed (16 §5.2 Warn; an accusation says it to their face).
                    belief.ToldTo.Add(conv.Player.Value);
                    var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Rumor, conv.Npc.Value, dp.Id, Salt.Initiative));
                    Rumors.Hear(world, player, npc, belief.Claim, belief.C, belief.Hop + 1, ref rng);
                    if (chosen.Id == "warn") { world.Relationships.TrustEvidence(conv.Player, conv.Npc, 2f); }
                }

                break;
        }

        world.Emit(Salience.Minor, conv.Npc, new InitiativeTaken(conv.Id, conv.Npc, chosen.Id, chosen.Params));
    }

    private static void Sources(SimWorld world, int npc, int player, Conversation conv, List<(string Id, float W, OptionParam[] Params)> into)
    {
        var people = world.People;
        ref readonly var n = ref people.Needs[npc];
        ref readonly var e = ref people.Emotions[npc];
        ref readonly var p = ref people.Personality[npc];
        EntityId me = people.Ids[npc], them = people.Ids[player];

        // needs → ask_favor: they were at a task and are tiring.
        if (conv.PrevAction >= 0 && n.Energy < 80f)
        {
            into.Add(("ask_favor", 0.5f + (2f * (100f - n.Energy) / 100f), [new("task", conv.PrevAction), new("hours", 1)]));
        }

        // emotions → challenge.
        var honor = p.Values.Honor >= 60 && world.Relationships.TryGet(me, them, out var edge)
                    && edge.Mods.Exists(m => world.Content.OpinionModifiers[m.Modifier].Id == "opinion.insulted_me");
        if (e.AngerTarget == them && e.Anger >= 50f) { into.Add(("challenge", 1f + (4f * (e.Anger - 50f) / 50f), [])); }
        else if (honor) { into.Add(("challenge", 1.5f, [])); }

        // beliefs → accuse (a misdeed by the player), warn (something done to the player), share_gossip.
        Belief? accuse = null, warn = null;
        foreach (var b in world.Beliefs.Span(me))
        {
            ref readonly var c = ref world.Claims[b.Claim];
            if (world.Content.ClaimPredicates[c.Predicate].Valence != ClaimValence.Negative || b.ToldTo.Contains(them.Value)) { continue; }
            if (c.Subject == them.Value && b.C >= 0.7f && (accuse is null || b.C > accuse.C)) { accuse = b; }
            if (c.Object == them.Value && c.Subject != me.Value && b.C >= 0.5f && (warn is null || b.C > warn.C)) { warn = b; }
        }

        if (accuse is not null)
        {
            var j = world.Content.ClaimPredicates[world.Claims[accuse.Claim].Predicate].Juiciness;
            into.Add(("accuse", 1f + (2f * accuse.C * j), [new("claim", accuse.Claim)]));
        }

        if (warn is not null && world.Relationships.Opinion(me, them) >= 20f) { into.Add(("warn", 1.5f, [new("claim", warn.Claim)])); }

        var gossipy = p.Sociability >= 65 || p.HasTrait(world.Content.TraitHandle("trait.gossip"));
        if (gossipy && Rumors.BestTellable(world, npc, player) is ({ } topic, var tell) && tell > 0f && topic != accuse && topic != warn)
        {
            into.Add(("share_gossip", 1f + tell, [new("claim", topic.Claim)]));
        }

        // the evening gathering → invite (Landfall: the social block at the fire from 19:00).
        var minuteOfDay = world.Clock.GameMinute % 1440;
        if (n.Social < 60f && minuteOfDay is >= 12 * 60 and < 19 * 60)
        {
            into.Add(("invite", 0.5f + (2f * ActivitySystem.Urgency("social", n.Social)), [new("at_min", 19 * 60)]));
        }
    }

    /// <summary>The top 3 candidates by propensity (score with character terms), in a stable order.</summary>
    private static List<(string Id, float W, OptionParam[] Params)> TopByPropensity(SimWorld world, int npc, int player, DecisionDef def,
        List<(string Id, float W, OptionParam[] Params)> candidates, float noneMass, float agenda)
    {
        if (candidates.Count <= MaxInitiatives) { return candidates; }
        var (p, _) = Propensities(world, npc, player, def, candidates, noneMass, agenda);
        return [.. candidates.Select((c, k) => (c, P: p[k + 1])).OrderByDescending(x => x.P).ThenBy(x => x.c.Id, StringComparer.Ordinal).Take(MaxInitiatives).Select(x => x.c)];
    }

    /// <summary>21 §7.8 over [none, candidates…]: each option its own family with mass B; T spreads them. p[0] is none.</summary>
    private static (float[] P, MenuPropensities Raw) Propensities(SimWorld world, int npc, int player, DecisionDef def,
        List<(string Id, float W, OptionParam[] Params)> candidates, float noneMass, float agenda)
    {
        var inputs = new List<PropensityInput> { new() { Id = "none", Family = "none" } };
        var families = new List<FamilyBase> { new() { Family = "none", Mass = noneMass, Includes = AllTerms } };
        foreach (var (id, w, _) in candidates)
        {
            var o = def.Options.First(x => x.Id == id);
            inputs.Add(new PropensityInput
            {
                Id = id, Family = "init." + id, UtilityKey = o.UtilityKey,
                Facet = o.FacetK is { Count: 1 } fk ? (fk.Keys.First(), fk.Values.First()) : null,
                ValueTags = o.ValueTags, Direction = o.Direction, Need = o.Need, Outlet = o.Outlet,
            });
            families.Add(new FamilyBase { Family = "init." + id, Mass = w * agenda / ThetaInit });
        }

        var opinion = world.Relationships.Opinion(world.People.Ids[npc], world.People.Ids[player]);
        var raw = Propensity.Compute(world, npc, inputs, families, opinion);
        var p = new float[inputs.Count];
        for (var k = 0; k < inputs.Count; k++) { p[k] = raw.P[Array.IndexOf(raw.Ids, inputs[k].Id)]; }
        return (p, raw);
    }

    /// <summary>21 §14.4: <c>logistic(10·(best alternative / score(converse) − 1.3))</c>; 0.05 with no utility AI.</summary>
    public static float PEnd(SimWorld world, int npc)
    {
        var activity = world.Systems.OfType<ActivitySystem>().FirstOrDefault();
        if (activity is null) { return 0.05f; }
        var ratio = activity.StayRatio(world, npc);
        return 1f / (1f + SimMath.Exp(-10f * (ratio - 1.3f)));
    }

    /// <summary>Why the NPC would go (the best alternative action's handle, for the line: "I've work to finish").</summary>
    private static long EndReason(SimWorld world, int npc)
        => world.Systems.OfType<ActivitySystem>().FirstOrDefault()?.BestAlternative(world, npc) ?? -1;

    private static long Param(MenuOption o, string key)
    {
        foreach (var p in o.Params) { if (p.Key == key) { return p.Value; } }
        return -1;
    }

    private static MenuOption Option(DecisionDef def, string id, OptionParam[] ps, float p, SimWorld world, int player)
    {
        var o = def.Options.First(x => x.Id == id);
        var gloss = o.Gloss.Replace("{player}", player >= 0 ? world.People.Names[player] : "them", StringComparison.Ordinal);
        foreach (var param in ps)
        {
            var text = param.Key switch
            {
                "task" when param.Value >= 0 && param.Value < world.Content.Actions.Count => world.Content.Actions[(int)param.Value].Name.ToLowerInvariant(),
                "claim" when param.Value >= 0 && param.Value < world.Claims.Count => ClaimText(world, (int)param.Value),
                "reason" when param.Value >= 0 && param.Value < world.Content.Actions.Count => "they need to " + world.Content.Actions[(int)param.Value].Name.ToLowerInvariant(),
                "reason" => "they have things to do",
                _ => param.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            gloss = gloss.Replace("{" + param.Key + "}", text, StringComparison.Ordinal);
        }

        return new MenuOption(id, o.Family, ps, true, p, 0f, o.Stakes, o.FavorsPlayer, gloss);
    }

    private static string ClaimText(SimWorld world, int claim)
    {
        ref readonly var c = ref world.Claims[claim];
        var phrase = world.Content.ClaimPredicates[c.Predicate].Phrase;
        string Name(ulong id) => world.People.IndexOf(new EntityId(id)) is var r and >= 0 ? world.People.Names[r] : "someone";
        return phrase.Replace("{subject}", Name(c.Subject), StringComparison.Ordinal).Replace("{object}", Name(c.Object), StringComparison.Ordinal);
    }
}
