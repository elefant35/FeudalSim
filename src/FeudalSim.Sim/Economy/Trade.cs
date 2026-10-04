using System.IO.Hashing;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;
using MessagePack;

namespace FeudalSim.Sim.Economy;

/// <summary>
/// What people own (M1 prototype for 15's purses and 14/15's inventories): coin in farthings and goods by item handle.
/// Saved and hashed. Real containers, quality per stack and household stores arrive in M2–M3.
/// </summary>
public sealed class Holdings
{
    private readonly SortedDictionary<ulong, long> _coin = [];
    private readonly SortedDictionary<(ulong Person, int Item), int> _goods = [];

    public long Coin(EntityId person) => _coin.GetValueOrDefault(person.Value);

    public int Goods(EntityId person, int item) => _goods.GetValueOrDefault((person.Value, item));

    internal void Set(EntityId person, int item, int qty, long coin)
    {
        if (item >= 0) { if (qty > 0) { _goods[(person.Value, item)] = qty; } else { _goods.Remove((person.Value, item)); } }
        if (coin > 0) { _coin[person.Value] = coin; } else { _coin.Remove(person.Value); }
    }

    /// <summary>Moves goods from the seller and coin from the buyer at the agreed price. False if either can't.</summary>
    internal bool Settle(EntityId seller, EntityId buyer, int item, int qty, long price)
    {
        if (Goods(seller, item) < qty || Coin(buyer) < price) { return false; }
        Add(seller, item, -qty);
        Add(buyer, item, qty);
        _coin[buyer.Value] = Coin(buyer) - price;
        _coin[seller.Value] = Coin(seller) + price;
        return true;
    }

    /// <summary>Moves goods without payment (a theft, 16 §10). False if the holder lacks them.</summary>
    internal bool Take(EntityId from, EntityId to, int item, int qty)
    {
        if (qty <= 0 || Goods(from, item) < qty) { return false; }
        Add(from, item, -qty);
        Add(to, item, qty);
        return true;
    }

    private void Add(EntityId person, int item, int delta)
    {
        var q = Goods(person, item) + delta;
        if (q > 0) { _goods[(person.Value, item)] = q; } else { _goods.Remove((person.Value, item)); }
    }

    public struct CoinRow { public ulong Person; public long Coin; }

    public struct GoodsRow { public ulong Person; public int Item, Qty; }

    internal (CoinRow[] Coin, GoodsRow[] Goods) Export()
        => ([.. _coin.Select(kv => new CoinRow { Person = kv.Key, Coin = kv.Value })], [.. _goods.Select(kv => new GoodsRow { Person = kv.Key.Person, Item = kv.Key.Item, Qty = kv.Value })]);

    internal void Import(CoinRow[] coin, GoodsRow[] goods)
    {
        _coin.Clear();
        _goods.Clear();
        foreach (var c in coin) { _coin[c.Person] = c.Coin; }
        foreach (var g in goods) { _goods[(g.Person, g.Item)] = g.Qty; }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var (p, c) in _coin) { BitConverter.TryWriteBytes(b, (long)p); h.Append(b); BitConverter.TryWriteBytes(b, c); h.Append(b); }
        foreach (var ((p, i), q) in _goods) { BitConverter.TryWriteBytes(b, (long)p); h.Append(b); BitConverter.TryWriteBytes(b, ((long)i << 32) | (uint)q); h.Append(b); }
    }
}

/// <summary>
/// A haggle between the player and an NPC over one item (15 §5.1–5.7): the NPC's valuation, curve and patience fixed when
/// it opens, the round, the standing offers, the granted step (it ratchets) and the words signal Σσ. Saved and hashed.
/// </summary>
[MessagePackObject]
public sealed class Negotiation
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public EntityId Npc { get; set; }
    [Key(2)] public EntityId Player { get; set; }
    [Key(3)] public int Item { get; set; }
    [Key(4)] public int Qty { get; set; }

    /// <summary>True when the NPC is the seller (the player buys); false when the player is selling to the NPC.</summary>
    [Key(5)] public bool NpcSells { get; set; }

    [Key(6)] public float Pv { get; set; }
    [Key(7)] public float Rv { get; set; }
    [Key(8)] public float Asp { get; set; }
    [Key(9)] public int Patience { get; set; }
    [Key(10)] public float Beta { get; set; }
    [Key(11)] public float Tau { get; set; }
    [Key(12)] public float Budget { get; set; } = float.MaxValue;
    [Key(13)] public int Round { get; set; }
    [Key(14)] public int GrantedStep { get; set; }
    [Key(15)] public float Sigma { get; set; }
    [Key(16)] public long NpcOffer { get; set; }
    [Key(17)] public long PlayerOffer { get; set; }
    [Key(18)] public int Insults { get; set; }
    [Key(19)] public int Persuasive { get; set; }
    [Key(20)] public List<string> UsedArguments { get; set; } = [];
    [Key(21)] public float Gap { get; set; }
}

public sealed class NegotiationStore
{
    private readonly SortedDictionary<ulong, Negotiation> _open = [];
    private ulong _lastId;

    public int Count => _open.Count;

    public Negotiation? Get(ulong id) => _open.TryGetValue(id, out var n) ? n : null;

    public IEnumerable<Negotiation> Open => _open.Values;

    internal Negotiation Add(Negotiation n)
    {
        n.Id = ++_lastId;
        _open.Add(n.Id, n);
        return n;
    }

    internal bool Remove(ulong id) => _open.Remove(id);

    internal (Negotiation[] Open, ulong LastId) Export() => ([.. _open.Values], _lastId);

    internal void Import(Negotiation[] open, ulong lastId)
    {
        _open.Clear();
        foreach (var n in open) { _open.Add(n.Id, n); }
        _lastId = lastId;
    }

    internal void HashInto(XxHash64 h) => h.Append(MessagePackSerializer.Serialize(_open.Values.ToArray()));
}

/// <summary>
/// 15 §5.6 trade DP (kind <c>trade.respond</c>; content <c>dp.trade_sell</c> / <c>dp.trade_buy</c>): each round the NPC answers
/// the player's bid (or ask) from the trade system's fixed prices; the LLM in the reply picks, the DRE guards and this owner
/// executes — a deal settles at the option's price, moving goods and coin. Commands stand in for the trade UI: open,
/// offer (with the structured haggle arguments of template mode, §5.6), accept the NPC's offer, walk away.
/// </summary>
public sealed class TradeOwner : IDecisionPointOwner
{
    public const string Id = "15.trade";
    public const string Kind = "trade.respond";

    public string OwnerId => Id;

    public IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context)
    {
        var n = world.Negotiations.Get((ulong)context.Subject);
        var people = world.People;
        if (n is null || people.IndexOf(n.Npc) < 0) { return [new("walk_away", "end", [], true, 1f, 0f, Stakes.Low, false, "walk away")]; }
        var def = world.Content.Decision(n.NpcSells ? "dp.trade_sell" : "dp.trade_buy") ?? throw new InvalidOperationException("Content has no trade menus.");
        var npc = people.IndexOf(n.Npc);
        var menu = Haggle.Menu(RoundOf(world, n, npc), n.NpcSells, n.Budget);
        var item = world.Content.Items[n.Item];
        var player = people.IndexOf(n.Player);
        return [.. menu.Select(o =>
        {
            var d = def.Options.First(x => x.Id == o.Id);
            OptionParam[] ps = o.PriceF > 0 ? [new("price_f", o.PriceF), new("qty", n.Qty)] : [];
            var gloss = d.Gloss.Replace("{player}", player >= 0 ? people.Names[player] : "them", StringComparison.Ordinal)
                .Replace("{item}", item.Name.ToLowerInvariant(), StringComparison.Ordinal).Replace("{price}", Money.Words(o.PriceF), StringComparison.Ordinal);
            var stakes = o.Id is "refuse" or "walk_away" ? Stakes.Low : Money.Stakes(o.PriceF);
            var favors = o.Id is "accept_offer" or "buy_at_ask";
            return new MenuOption(o.Id, d.Family, ps, o.Eligible, o.P, 0f, stakes, favors, gloss);
        })];
    }

    public void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen)
    {
        if (world.Negotiations.Get((ulong)dp.Context.Subject) is not { } n) { return; }
        var price = chosen.Params.FirstOrDefault(p => p.Key == "price_f").Value;
        switch (chosen.Id)
        {
            case "accept_offer" or "buy_at_ask":
                Settle(world, n, n.PlayerOffer);
                return;

            case "walk_away":
                End(world, n, "npc_walked");
                return;

            case "refuse":
                n.Round++;
                break;

            default:   // counter_step_n
                n.NpcOffer = price;
                n.GrantedStep = Math.Max(n.GrantedStep, (int)(chosen.Id[^1] - '0'));
                n.Round++;
                break;
        }

        world.Emit(Salience.Minor, n.Npc, new TradeOffered(n.Id, n.Npc, n.NpcOffer, chosen.Id));
    }

    private static Haggle.Round RoundOf(SimWorld world, Negotiation n, int npc)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[npc];
        var hot = p.HasTrait(world.Content.TraitHandle("trait.hot_tempered"));
        var (rv, asp) = Hardened(world, n, npc);
        var buyerCanPay = n.NpcSells ? world.Holdings.Coin(n.Player) >= n.PlayerOffer : world.Holdings.Coin(n.Npc) >= n.PlayerOffer;
        var sellerHas = n.NpcSells ? world.Holdings.Goods(n.Npc, n.Item) >= n.Qty : world.Holdings.Goods(n.Player, n.Item) >= n.Qty;
        return new Haggle.Round(rv, asp, n.Patience, n.Beta, Margin(world, n, npc), n.Round, n.GrantedStep, n.Sigma, n.PlayerOffer, n.NpcOffer,
            people.Emotions[npc].AngerTarget == n.Player ? people.Emotions[npc].Anger : 0f, hot || p.Volatility >= 65, p.HasTrait(world.Content.TraitHandle("trait.stubborn")),
            buyerCanPay && sellerHas, Gap: n.Gap, Tau: n.Tau);
    }

    /// <summary>While Σσ &lt; 0 (an argument backfired) the NPC's curve hardens by |Σσ|·Margin, at most half a margin (§5.6).</summary>
    private static (float Rv, float Asp) Hardened(SimWorld world, Negotiation n, int npc)
    {
        if (n.Sigma >= 0f) { return (n.Rv, n.Asp); }
        var h = MathF.Min(-n.Sigma, 0.5f) * Margin(world, n, npc);
        return n.NpcSells ? (n.Rv * (1f + h), n.Asp * (1f + h)) : (n.Rv * (1f - h), n.Asp * (1f - h));
    }

    /// <summary>15 §5.5–5.6: Margin = 0.15 · S_n · (0.5 + 0.5·K_skill), recomputed each round (opinion, mood and anger move).</summary>
    public static float Margin(SimWorld world, Negotiation n, int npc) => Haggle.Margin(Susceptibility(world, n, npc), KSkill(world, world.People.IndexOf(n.Player)));

    public static float Susceptibility(SimWorld world, Negotiation n, int npc)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[npc];
        var player = people.IndexOf(n.Player);
        var rel = world.Relationships;
        bool Has(string t) => world.People.Personality[npc].HasTrait(world.Content.TraitHandle(t));
        return Haggle.Susceptibility(p.Warmth, rel.Opinion(n.Npc, n.Player), rel.Trust(n.Npc, n.Player), people.Mood[npc].Smoothed,
            Skill(world, npc, "skill.commerce"), player >= 0 ? Skill(world, player, "skill.commerce") : 0f, Has("trait.stubborn"), Has("trait.greedy"), Has("trait.paranoid"),
            people.Emotions[npc].AngerTarget == n.Player ? people.Emotions[npc].Anger : 0f);
    }

    public static float KSkill(SimWorld world, int speaker) => speaker < 0 ? 0f : Haggle.KSkill(Skill(world, speaker, "skill.persuasion"), Skill(world, speaker, "skill.commerce"));

    public static float Skill(SimWorld world, int row, string id)
    {
        var h = world.Content.SkillHandle(id);
        return h < 0 ? 0f : world.People.SkillLevels(row)[h];
    }

    // ---- commands (the trade UI) ----------------------------------------------------------------------------------------

    /// <summary>Opens a haggle over <c>Qty</c> of an item: the NPC's valuation, curve and patience are fixed now (§5.2–5.3).</summary>
    internal static void Open(SimWorld world, in CommandEnvelope command, TradeOpen c)
    {
        var people = world.People;
        var npc = people.IndexOf(c.Npc);
        var player = world.PlayerRow;
        var item = ContentDatabase.HandleOf(world.Content.Items, c.Item, i => i.Id);
        string? problem = player < 0 ? "no player character" : npc < 0 || npc == player ? "no such person" : item < 0 ? $"unknown item {c.Item}" : c.Qty < 1 ? "quantity" : null;
        if (problem is null && world.Conversations.Of(c.Npc) is null) { problem = "not in a conversation with them"; }
        var seller = c.PlayerSells ? world.PlayerId : c.Npc;
        if (problem is null && world.Holdings.Goods(seller, item) < c.Qty) { problem = "the seller doesn't have it"; }
        var opinion = problem is null ? world.Relationships.Opinion(c.Npc, world.PlayerId) : 0f;
        var social = Haggle.SellerSocialFactor(opinion);
        if (problem is null && social is null) { problem = "they won't trade with you"; }
        if (problem is not null) { world.RejectCommand(command, $"TradeOpen: {problem}."); return; }

        ref readonly var p = ref people.Personality[npc];
        bool Has(string t) => world.People.Personality[npc].HasTrait(world.Content.TraitHandle(t));
        var value = (float)world.Content.Items[item].BaseValueF * c.Qty;   // ItemValue: base value × QualityMult(Q 50) = 1 (local prices: M3–M4)
        var commerce = Skill(world, npc, "skill.commerce");
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Economy, c.Npc.Value, (ulong)item, (ulong)(world.Clock.GameMinute / 1440)));
        var sigma = 0.03f + (0.25f * (1f - (commerce / 100f)));   // appraisal error (15 §2.4)
        var pv = value * (1f + (sigma * rng.NextNormal()));
        var stranger = world.Relationships.Familiarity(c.Npc, world.PlayerId) < 20f;
        var n = new Negotiation
        {
            Npc = c.Npc, Player = world.PlayerId, Item = item, Qty = c.Qty, NpcSells = !c.PlayerSells, Pv = pv,
            Patience = Haggle.Patience(p.Sociability, p.Diligence, p.Volatility, Has("trait.hot_tempered"), marketDay: false),
            Beta = Haggle.Firmness(p.Warmth, Has("trait.stubborn"), Has("trait.greedy"), Has("trait.charitable")),
            Tau = Haggle.InsultTolerance(p.Volatility >= 65 || Has("trait.hot_tempered"), p.Warmth >= 65, opinion >= 40f, Has("trait.greedy")),
        };
        if (n.NpcSells)
        {
            // An NPC selling what they own: no cost floor recorded yet (crafting provenance: M2–M3), urgency 0.05.
            n.Rv = Haggle.SellerRv(0f, pv, 0.05f) * social!.Value;
            n.Asp = Haggle.SellerAsp(pv, Haggle.SellerMargin(Has("trait.greedy"), p.Values.Wealth, stranger, competitors: false)) * social.Value;
            n.NpcOffer = Haggle.Round2(n.Asp);   // the opening ask is deterministic: nothing has been said yet
        }
        else
        {
            // Being pitched: an NPC who already owns one values another only at resale (§5.6); budget = their coin.
            var owns = world.Holdings.Goods(c.Npc, item) > 0;
            var pvUse = owns ? pv * 0.85f : pv;
            n.Budget = world.Holdings.Coin(c.Npc);
            n.Rv = Haggle.BuyerRv(n.Budget, pvUse, 0.05f);
            n.Asp = Haggle.BuyerAsp(pvUse, Haggle.BuyerMargin(Has("trait.greedy"), commerce));
            n.NpcOffer = 0;
        }

        world.Negotiations.Add(n);
        world.Emit(Salience.Minor, c.Npc, new TradeOffered(n.Id, c.Npc, n.NpcOffer, "open"));
    }

    /// <summary>
    /// The player's offer (a bid when buying, an ask when selling) with optional structured arguments (§5.6 template path)
    /// and their quality W: words → Σσ; the lowball/insult rules run (acts, before any menu); then the NPC's DP opens.
    /// </summary>
    internal static void Offer(SimWorld world, in CommandEnvelope command, TradeOffer c)
    {
        if (world.Negotiations.Get(c.Negotiation) is not { } n) { world.RejectCommand(command, $"TradeOffer: no negotiation {c.Negotiation}."); return; }
        var npc = world.People.IndexOf(n.Npc);
        var player = world.People.IndexOf(n.Player);
        if (npc < 0 || player < 0 || c.PriceF <= 0) { world.RejectCommand(command, "TradeOffer: invalid."); return; }
        n.PlayerOffer = c.PriceF;

        // Step 3–4: arguments against ground truth, the words signal, backfires as acts.
        var args = (c.Arguments ?? []).Distinct(StringComparer.Ordinal).Take(2).Select(a => new Argument(a, Validity(world, n, npc, player, a))).ToList();
        if (args.Count > 0)
        {
            var w = c.Quality > 0f ? Math.Clamp(c.Quality, 0f, 1f) : 0.5f;   // template mode: W = 0.5
            var sigma = Haggle.Utterance(args, w, KSkill(world, player), n.Persuasive, n.UsedArguments.ToHashSet(StringComparer.Ordinal));
            if (sigma != 0f) { n.Persuasive++; }
            n.Sigma = Math.Clamp(n.Sigma + sigma, -0.5f, 1f);
            foreach (var a in args)
            {
                if (!n.UsedArguments.Contains(a.Kind)) { n.UsedArguments.Add(a.Kind); }
                if (a.Validity < 0f) { world.Relationships.ApplyModifier(n.Npc, n.Player, "opinion.false_argument"); }
            }
        }

        // §5.4 lowball: an offer far outside the NPC's zone is an insult — deterministic, before any menu.
        n.Gap = n.NpcSells ? (n.Rv - c.PriceF) / n.Rv : (c.PriceF - n.Rv) / MathF.Max(1f, n.Rv);
        var (anger, opinion) = Haggle.Lowball(n.Gap, n.Tau);
        if (anger > 0f && n.NpcSells)
        {
            ref var e = ref world.People.Emotions[npc];
            (e.Anger, e.AngerTarget) = (MathF.Min(100f, e.Anger + anger), n.Player);
            world.Relationships.ApplyModifier(n.Npc, n.Player, "opinion.lowballed_me", -opinion / 5f);   // value 5 × multiplier = 5 + 20·(g − τ)
            n.Patience = Math.Max(0, n.Patience - 2);
            if (++n.Insults >= 2) { End(world, n, "insulted"); return; }
            if (e.Anger >= 80f && (world.People.Personality[npc].HasTrait(world.Content.TraitHandle("trait.hot_tempered")) || world.People.Personality[npc].Volatility >= 75))
            {
                Social.Escalation.Provoke(world, player, npc, 3, DeciderKind.Llm, DecisionRulesEngine.ConversationDeadlineSteps);
            }
        }

        // Step 7: the NPC's DP. One eligible option → executed without a DP (the policy, inline).
        var menu = Haggle.Menu(RoundOf(world, n, npc), n.NpcSells, n.Budget);
        var decider = menu.Count(o => o.Eligible) <= 1 ? DeciderKind.Policy : DeciderKind.Llm;
        var dp = world.Decisions.Open(Id, new DpContext(Kind, n.Npc, n.Player, (long)n.Id), decider, DecisionRulesEngine.ConversationDeadlineSteps);
        if (world.Decisions.IsOpen(dp) && world.Conversations.Of(n.Npc) is { } conv) { conv.Dps.Add(dp); }
    }

    internal static void AcceptNpcOffer(SimWorld world, in CommandEnvelope command, TradeAccept c)
    {
        if (world.Negotiations.Get(c.Negotiation) is not { } n || n.NpcOffer <= 0) { world.RejectCommand(command, "TradeAccept: nothing to accept."); return; }
        Settle(world, n, n.NpcOffer);
    }

    internal static void WalkAway(SimWorld world, in CommandEnvelope command, TradeWalkAway c)
    {
        if (world.Negotiations.Get(c.Negotiation) is not { } n) { world.RejectCommand(command, "TradeWalkAway: no negotiation."); return; }
        End(world, n, "player_walked");   // 15 §5.3's call-back DP joins with proposals (M3)
    }

    private static void Settle(SimWorld world, Negotiation n, long price)
    {
        var (seller, buyer) = n.NpcSells ? (n.Npc, n.Player) : (n.Player, n.Npc);
        if (!world.Holdings.Settle(seller, buyer, n.Item, n.Qty, price)) { End(world, n, "cannot_pay"); return; }
        world.Relationships.ApplyModifier(n.Npc, n.Player, "opinion.fair_trade");
        world.Relationships.ApplyModifier(n.Player, n.Npc, "opinion.fair_trade");
        var value = world.Content.Items[n.Item].BaseValueF * n.Qty;
        if (n.NpcSells ? price <= 0.9f * value : price >= 1.1f * value) { world.Relationships.ApplyModifier(n.Player, n.Npc, "opinion.generous_deal"); }
        world.Emit(Salience.Notable, n.Npc, new TradeSettled(n.Id, seller, buyer, n.Item, n.Qty, price));
        world.Negotiations.Remove(n.Id);
    }

    private static void End(SimWorld world, Negotiation n, string reason)
    {
        world.Negotiations.Remove(n.Id);
        world.Emit(Salience.Minor, n.Npc, new NegotiationEnded(n.Id, n.Npc, reason));
    }

    /// <summary>15 §5.6 Step 3 validity in M1 (ground truth available now; competitor prices, flaws and repeat custom need M3–M4 data).</summary>
    public static float Validity(SimWorld world, Negotiation n, int npc, int player, string kind)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[npc];
        var op = world.Relationships.Opinion(n.Npc, n.Player);
        return kind switch
        {
            "quality_flaw" => Skill(world, npc, "skill.commerce") >= 40f ? -1f : 0.5f,   // all M1 goods are sound (Q 50): a judge sees through it
            "competitor_price" => 0.5f,                                                   // no price beliefs yet
            "hardship" => people.Needs[player].Satiety < 40f ? 1f : world.Holdings.Coin(n.Player) > 5 * world.Content.Items[n.Item].BaseValueF ? -1f : 0.5f,
            "relationship" => op >= 20f ? 1f : op >= 0f ? 0.5f : -1f,
            "future_business" => 0.5f,                                                    // trade history: M3
            "flattery" => p.Values.Status >= 60 ? 1f : p.HasTrait(world.Content.TraitHandle("trait.honest")) && p.Volatility >= 60 ? -1f : 0.5f,
            "bulk_deal" => n.Qty >= 3 ? 1f : -1f,
            _ => 0f,
        };
    }
}

/// <summary>Money words and stakes bands (canon §11, 15 §5.6).</summary>
public static class Money
{
    /// <summary>Stakes by the money an option binds: low &lt; 12f · medium 12–47f · high 48–959f · critical ≥ 960f.</summary>
    public static Stakes Stakes(long f) => f >= 960 ? Decisions.Stakes.Critical : f >= 48 ? Decisions.Stakes.High : f >= 12 ? Decisions.Stakes.Medium : Decisions.Stakes.Low;

    /// <summary>Farthings as words: 4f = 1 penny, 12d = 1 shilling (48f), 20s = 1 crown (960f).</summary>
    public static string Words(long f)
    {
        if (f <= 0) { return "nothing"; }
        long crowns = f / 960, shillings = f % 960 / 48, pence = f % 48 / 4, farthings = f % 4;
        var parts = new List<string>();
        if (crowns > 0) { parts.Add($"{crowns} crown{(crowns == 1 ? "" : "s")}"); }
        if (shillings > 0) { parts.Add($"{shillings} shilling{(shillings == 1 ? "" : "s")}"); }
        if (pence > 0) { parts.Add($"{pence} penn{(pence == 1 ? "y" : "ies")}"); }
        if (farthings > 0) { parts.Add($"{farthings} farthing{(farthings == 1 ? "" : "s")}"); }
        return string.Join(" and ", parts);
    }
}
