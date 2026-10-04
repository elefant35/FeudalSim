using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Economy;

/// <summary>One bargaining argument and its validity against ground truth (15 §5.6 Step 3): +1, +0.5 or −1 (a backfire).</summary>
public readonly record struct Argument(string Kind, float Validity);

/// <summary>An option of a trade menu with its fixed price and base propensity (15 §5.6 Steps 5–6).</summary>
public readonly record struct TradeOption(string Id, bool Eligible, long PriceF, float P, int Step);

/// <summary>
/// 15 §5 haggling math, pure and deterministic: reservation values and aspirations (§5.2), patience, firmness and the
/// concession curve (§5.3), the insult tolerance (§5.4), susceptibility (§5.5), the words signal Σσ and the menu with its
/// base propensities (§5.6). Prices are farthings; menu prices round half away from zero.
/// </summary>
public static class Haggle
{
    public const float CSys = 0.15f;

    // ---- §5.2 -----------------------------------------------------------------------------------------------------------

    public static float SellerRv(float costFloor, float pv, float urgency) => MathF.Max(costFloor * (1f - (0.5f * urgency)), pv * (1f - urgency));

    public static float SellerAsp(float pv, float margin) => pv * (1f + margin);

    public static float BuyerRv(float budget, float pv, float urgency) => MathF.Min(budget, pv * (1f + urgency));

    public static float BuyerAsp(float pv, float margin) => pv * (1f - margin);

    /// <summary><c>m_s = 0.15 + 0.15·Greedy + (Wealth − 50)/500 + 0.10·[stranger] − 0.05·[competing sellers]</c>.</summary>
    public static float SellerMargin(bool greedy, float wealthValue, bool stranger, bool competitors)
        => 0.15f + (greedy ? 0.15f : 0f) + ((wealthValue - 50f) / 500f) + (stranger ? 0.10f : 0f) - (competitors ? 0.05f : 0f);

    /// <summary><c>m_b = 0.15 + 0.15·Greedy + (Commerce − 50)/500</c>.</summary>
    public static float BuyerMargin(bool greedy, float commerce) => 0.15f + (greedy ? 0.15f : 0f) + ((commerce - 50f) / 500f);

    /// <summary>Social modifier on a seller's prices (×) — friends discount up to 10 %, dislike surcharges; null = refuses (Op ≤ −50).</summary>
    public static float? SellerSocialFactor(float opinion)
        => opinion <= -50f ? null : opinion > 0f ? 1f - (0.10f * opinion / 100f) : opinion <= -25f ? 1f + (0.20f * -opinion / 100f) : 1f;

    // ---- §5.3 -----------------------------------------------------------------------------------------------------------

    public static int Patience(float sociability, float diligence, float volatility, bool hotTempered, bool marketDay)
        => Math.Clamp(4 + (sociability >= 65 ? 1 : 0) + (diligence >= 65 ? 1 : 0) - (volatility >= 65 ? 1 : 0) - (hotTempered ? 1 : 0) + (marketDay ? 0 : 1), 2, 7);

    public static float Firmness(float warmth, bool stubborn, bool greedy, bool charitable)
        => Math.Clamp(1f + ((50f - warmth) / 100f) + (stubborn ? 0.8f : 0f) + (greedy ? 0.4f : 0f) - (charitable ? 0.3f : 0f), 0.4f, 3f);

    /// <summary><c>O_k = Asp + (RV − Asp)·(k/K)^β</c>.</summary>
    public static float Offer(float asp, float rv, int k, int patience, float beta)
        => asp + ((rv - asp) * SimMath.Pow(Math.Clamp(k / (float)patience, 0f, 1f), beta));

    // ---- §5.4 -----------------------------------------------------------------------------------------------------------

    public static float InsultTolerance(bool volatileOrHot, bool warm, bool likes, bool greedy)
        => Math.Clamp(0.40f - (volatileOrHot ? 0.10f : 0f) + (warm ? 0.10f : 0f) + (likes ? 0.10f : 0f) - (greedy ? 0.10f : 0f), 0.15f, 0.60f);

    /// <summary>Anger gained and the "lowballed me" opinion change for a gap g past tolerance τ (0 if within it).</summary>
    public static (float Anger, float Opinion) Lowball(float gap, float tau)
        => gap <= tau ? (0f, 0f) : (10f + (40f * (gap - tau) / (1f - tau)), -(5f + (20f * (gap - tau))));

    // ---- §5.5 -----------------------------------------------------------------------------------------------------------

    /// <summary>15 §5.5 susceptibility S_n ∈ [0.05, 1].</summary>
    public static float Susceptibility(float warmth, float opinion, float trust, float mood, float commerceListener, float commerceSpeaker,
        bool stubborn, bool greedy, bool paranoid, float anger)
        => Math.Clamp(0.5f + ((warmth - 50f) / 200f) + (opinion / 400f) + ((trust - 50f) / 400f) + (mood / 400f)
                      - ((commerceListener - commerceSpeaker) / 400f) - (stubborn ? 0.20f : 0f) - (greedy ? 0.15f : 0f) - (paranoid ? 0.10f : 0f)
                      - (anger / 200f), 0.05f, 1f);

    /// <summary><c>K_skill = (0.5·Persuasion + 0.5·Commerce)/100</c>.</summary>
    public static float KSkill(float persuasion, float commerce) => ((0.5f * persuasion) + (0.5f * commerce)) / 100f;

    public static float Margin(float susceptibility, float kSkill) => CSys * susceptibility * (0.5f + (0.5f * kSkill));

    // ---- §5.6 Step 4: the words signal ----------------------------------------------------------------------------------

    /// <summary>
    /// One utterance's contribution: <c>σ_a = (W·v_a + K_skill)/2</c>; <c>σ_utt = σ_(1) + 0.6·σ_(2)</c> (best first), ×
    /// <c>0.6^(n_prior)</c>; 0 if every argument kind was already used.
    /// </summary>
    public static float Utterance(IReadOnlyList<Argument> args, float w, float kSkill, int priorPersuasive, IReadOnlySet<string> used)
    {
        if (args.Count == 0 || args.All(a => used.Contains(a.Kind))) { return 0f; }
        var sigmas = args.Select(a => ((w * a.Validity) + kSkill) / 2f).OrderByDescending(x => x).ToArray();
        var s = sigmas[0] + (sigmas.Length > 1 ? 0.6f * sigmas[1] : 0f);
        return s * MathF.Pow(0.6f, priorPersuasive);
    }

    // ---- §5.6 Steps 5–6: the menu ---------------------------------------------------------------------------------------

    /// <summary>Inputs to a seller's (or, mirrored, a buyer's) menu for one round.</summary>
    public readonly record struct Round(
        float Rv, float Asp, int Patience, float Beta, float Margin, int Round_, int GrantedStep, float SigmaTotal,
        long Offer, long LastOwn, float Anger, bool HotOrVolatile, bool Stubborn, bool CanSettle, float KIrr = 1f, float Gap = 0f, float Tau = 1f);

    /// <summary>The step prices: NPC seller RV_j = RV·(1 − j·M), Asp_j = Asp·(1 − j·M); NPC buyer RV_j = RV·(1 + j·M) (≤ budget), Asp_j likewise.</summary>
    public static (float Rv, float Asp) StepValues(float rv, float asp, float margin, int step, bool seller, float budget = float.MaxValue)
    {
        var j = step / 3f;
        return seller ? (rv * (1f - (j * margin)), asp * (1f - (j * margin))) : (MathF.Min(budget, rv * (1f + (j * margin))), asp * (1f + (j * margin)));
    }

    public static long Round2(float f) => (long)MathF.Round(f, MidpointRounding.AwayFromZero);

    /// <summary>
    /// 15 §5.6: the menu of an NPC answering the counterpart's offer. Seller: <c>accept_offer · counter_step_0…3 · refuse ·
    /// walk_away</c>; buyer: <c>buy_at_ask · counter_step_0…3 · refuse · walk_away</c>. Proposals (other item, bundle, credit,
    /// barter) need stock lists and debts (M3–M4) and are not offered in M1.
    /// </summary>
    public static TradeOption[] Menu(in Round r, bool seller, float budget = float.MaxValue)
    {
        var k = r.Round_;
        var floor = StepValues(r.Rv, r.Asp, r.Margin, 3, seller, budget).Rv;
        var acceptOk = r.CanSettle && (seller ? r.Offer >= floor : r.Offer <= floor);

        // Step weights around the policy's signal G, and §5.3's acceptance test at each step.
        var g = MathF.Max(r.GrantedStep / 3f, Math.Clamp(r.SigmaTotal, 0f, 1f));
        Span<float> w = stackalloc float[4];
        Span<bool> acc = stackalloc bool[4];
        Span<long> price = stackalloc long[4];
        Span<bool> stepOk = stackalloc bool[4];
        for (var n = 0; n < 4; n++)
        {
            var (rvj, aspj) = StepValues(r.Rv, r.Asp, r.Margin, n, seller, budget);
            var next = Offer(aspj, rvj, k + 1, r.Patience, r.Beta);
            stepOk[n] = n >= r.GrantedStep && k < r.Patience;
            w[n] = stepOk[n] ? SimMath.Exp(-((n - (3f * g)) * (n - (3f * g))) / 0.72f) : 0f;
            acc[n] = seller ? r.Offer >= next || (k >= r.Patience && r.Offer >= rvj) : r.Offer <= next || (k >= r.Patience && r.Offer <= rvj);
            price[n] = seller ? Math.Max(r.Offer + 1, Math.Min(r.LastOwn, Round2(next))) : Math.Min(r.Offer - 1, Math.Max(r.LastOwn, Round2(next)));
        }

        float sumW = 0f, sumAcc = 0f;
        for (var n = 0; n < 4; n++) { sumW += w[n]; }
        for (var n = 0; n < 4; n++) { if (sumW > 0f && acc[n]) { sumAcc += w[n] / sumW; } }
        if (k >= r.Patience)
        {
            // Patience gone: no more counters — §5.3's acceptance at any step's RV.
            for (var n = 0; n < 4; n++) { if (acc[n]) { sumAcc = 1f; } }
        }

        var a = acceptOk ? MathF.Max(sumAcc, 0.03f * r.KIrr) : 0f;
        var firm = Math.Clamp(0.10f + (0.15f * (r.Beta - 1f)) + (r.Stubborn ? 0.15f : 0f), 0.05f, 0.50f);
        var acceptId = seller ? "accept_offer" : "buy_at_ask";
        var options = new List<TradeOption>(7);
        if (k < r.Patience)
        {
            var walk = MathF.Min(0.9f, 0.02f + (r.Anger / 250f) + (r.HotOrVolatile ? 0.05f : 0f) + (!seller ? 0.6f * MathF.Min(1f, r.Gap / r.Tau) : 0f));
            var rest = 1f - walk;
            float sumCounter = 0f;
            for (var n = 0; n < 4; n++) { if (stepOk[n] && !acc[n]) { sumCounter += w[n]; } }
            options.Add(new(acceptId, acceptOk, r.Offer, rest * a, 3));
            for (var n = 0; n < 4; n++)
            {
                var p = stepOk[n] && !acc[n] && sumCounter > 0f ? rest * (1f - a) * (1f - firm) * w[n] / sumCounter : 0f;
                options.Add(new($"counter_step_{n}", stepOk[n], price[n], p, n));
            }

            options.Add(new("refuse", true, r.LastOwn, rest * (1f - a) * firm, r.GrantedStep));
            options.Add(new("walk_away", true, 0, walk, 0));
        }
        else
        {
            options.Add(new(acceptId, acceptOk, r.Offer, a, 3));
            for (var n = 0; n < 4; n++) { options.Add(new($"counter_step_{n}", false, price[n], 0f, n)); }
            options.Add(new("refuse", false, r.LastOwn, 0f, r.GrantedStep));
            options.Add(new("walk_away", true, 0, 1f - a, 0));
        }

        // Ineligible options carry nothing; renormalize what the eligible ones hold.
        var total = options.Where(o => o.Eligible).Sum(o => o.P);
        return [.. options.Select(o => o with { P = o.Eligible && total > 0f ? o.P / total : 0f })];
    }
}
