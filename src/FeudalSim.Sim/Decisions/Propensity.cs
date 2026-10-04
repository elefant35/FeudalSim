using FeudalSim.Sim.Content;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Decisions;

/// <summary>What an owning system tells the propensity layer about one menu option (21 §7.8 menu inputs).</summary>
public sealed record PropensityInput
{
    public required string Id { get; init; }
    public required string Family { get; init; }

    /// <summary>Action group for trait utility multipliers (21 §4.3 <c>U[x]</c>), e.g. "help", "confront", "trade".</summary>
    public string? UtilityKey { get; init; }

    /// <summary>Facet multiplier <c>1 + k·z</c> (21 §4.1), e.g. ("warmth", 0.30) for help/gift.</summary>
    public (string Facet, float K)? Facet { get; init; }

    /// <summary>Signed value tags −1…+1 for value alignment V (21 §4.2).</summary>
    public IReadOnlyDictionary<string, float>? ValueTags { get; init; }

    /// <summary>+1 favors the speaker, −1 hostile toward them, 0 neutral (the R term).</summary>
    public int Direction { get; init; }

    /// <summary>Need the option satisfies (N = 1 + 0.5·u).</summary>
    public string? Need { get; init; }

    /// <summary>For options that start an activity: schedule fit (+1 / 0 / −0.5 / −1) and whether it abandons the current one.</summary>
    public float? ScheduleFit { get; init; }

    public bool AbandonsCurrent { get; init; }

    /// <summary>Emotion this option is an outlet for (hijack fold-in, 21 §8.3): anger, fear, grief, shame, jealousy, joy.</summary>
    public string? Outlet { get; init; }
}

/// <summary>An option family's base mass <c>B_f</c> from the owning system, and the character terms it already models.</summary>
public sealed record FamilyBase
{
    public required string Family { get; init; }
    public required float Mass { get; init; }

    /// <summary>Terms <c>B_f</c> already includes, so this layer skips them: personality, emotion, need, opinion, schedule, anger, fear….</summary>
    public IReadOnlySet<string> Includes { get; init; } = new HashSet<string>();

    /// <summary>For acceptance families: the n-th request for the same thing this game day (mass × 0.5^(n−1), canon §13.4).</summary>
    public int Repetition { get; init; } = 1;
}

/// <summary>21 §7.8 output to the DRE: propensities over the eligible options, the factors behind them, and T.</summary>
public sealed record MenuPropensities(string[] Ids, float[] P, string[] KeyFactors, float T);

/// <summary>
/// The policy at decision points (21 §7.8): turns an owner's menu into base propensities p_i. Text-blind — a pure
/// function of sim state; player text never enters it. Families spread by temperature, so splitting "accept" into
/// variants never inflates acceptance:
/// <code>
/// w_f = B_f × max m_o × 0.5^(n−1);  T = clamp(τ/τ0, 0.25, 3);  p_f = w_f^(1/T) / Σ w_g^(1/T);  p_o = p_f × m_o / Σ_{o'∈f} m_o'
/// </code>
/// </summary>
public static class Propensity
{
    public static MenuPropensities Compute(SimWorld world, int chooser, IReadOnlyList<PropensityInput> options, IReadOnlyList<FamilyBase> families,
        float opinionOfSpeaker = 0f, float kIrr = 1f)
    {
        var people = world.People;
        var content = world.Content;
        ref readonly var p = ref people.Personality[chooser];
        ref readonly var e = ref people.Emotions[chooser];
        ref readonly var n = ref people.Needs[chooser];
        var baseOf = families.ToDictionary(f => f.Family, StringComparer.Ordinal);
        var factors = new List<(string Text, float Weight)>();

        // Character terms m_o, skipping what each family's base already models.
        var m = new float[options.Count];
        for (var k = 0; k < options.Count; k++)
        {
            var o = options[k];
            var includes = baseOf.TryGetValue(o.Family, out var fb) ? fb.Includes : new HashSet<string>();
            var mo = 1f;
            if (!includes.Contains("personality"))
            {
                var pv = 1f;
                if (o.Facet is { } f) { pv *= Math.Clamp(1f + (f.K * DecisionNoise.Z(FacetOf(p, f.Facet))), 0.25f, 2f); }
                if (o.UtilityKey is { } group) { pv *= TraitUtility(content, p.Traits, group); }
                pv *= ValueAlignment(p.Values, o.ValueTags);
                mo *= Math.Clamp(pv, 0.4f, 2f);
                Note(factors, pv, $"{o.Id}: personality ×{pv:0.00}");
            }

            if (!includes.Contains("emotion"))
            {
                var em = EmotionBias(o, e);
                mo *= em;
                Note(factors, em, $"{o.Id}: feelings ×{em:0.00}");
            }

            if (o.Need is { } need && !includes.Contains("need"))
            {
                var nn = 1f + (0.5f * ActivitySystem.Urgency(need, ActivitySystem.Need(n, need)));
                mo *= nn;
                Note(factors, nn, $"{o.Id}: {need} ×{nn:0.00}");
            }

            if (o.Direction != 0 && !includes.Contains("opinion"))
            {
                var r = 1f + (0.5f * o.Direction * opinionOfSpeaker / 100f);
                mo *= r;
                Note(factors, r, $"{o.Id}: opinion of speaker ×{r:0.00}");
            }

            if (o.ScheduleFit is { } fit && !includes.Contains("schedule"))
            {
                var s = Math.Clamp(1f + (0.35f * (1f + (0.3f * DecisionNoise.Z(p.Diligence))) * fit), 0.5f, 1.5f);
                var sm = s / (o.AbandonsCurrent ? 1.15f : 1f);
                mo *= sm;
                Note(factors, sm, $"{o.Id}: schedule ×{sm:0.00}");
            }

            m[k] = mo;
        }

        // Family weights, temperature, family spread, split within families.
        var tau = DecisionNoise.Tau(p, people.Mood[chooser], e, content, kIrr);
        var t = Math.Clamp(tau / DecisionNoise.Tau0, 0.25f, 3f);
        var famNames = options.Select(o => o.Family).Distinct(StringComparer.Ordinal).ToArray();
        var w = new float[famNames.Length];
        for (var fi = 0; fi < famNames.Length; fi++)
        {
            var fam = famNames[fi];
            var bf = baseOf.TryGetValue(fam, out var b) ? b : null;
            var mass = bf?.Mass ?? 1f;
            var maxM = 0f;
            for (var k = 0; k < options.Count; k++) { if (options[k].Family == fam) { maxM = MathF.Max(maxM, m[k]); } }
            w[fi] = mass * maxM * MathF.Pow(0.5f, Math.Max(0, (bf?.Repetition ?? 1) - 1));
        }

        var spread = w.Select(x => MathF.Pow(MathF.Max(0f, x), 1f / t)).ToArray();
        var total = spread.Sum();
        var probs = new float[options.Count];
        for (var fi = 0; fi < famNames.Length; fi++)
        {
            var pf = total > 0f ? spread[fi] / total : 1f / famNames.Length;
            var sumM = 0f;
            for (var k = 0; k < options.Count; k++) { if (options[k].Family == famNames[fi]) { sumM += m[k]; } }
            for (var k = 0; k < options.Count; k++)
            {
                if (options[k].Family == famNames[fi]) { probs[k] = sumM > 0f ? pf * m[k] / sumM : 0f; }
            }
        }

        FoldInHijack(world, chooser, options, families, probs, factors);
        if (MathF.Abs(t - 1f) > 0.05f) { factors.Add(($"temperament T {t:0.00} ({(t > 1f ? "unpredictable" : "steady")})", MathF.Abs(t - 1f))); }
        var key = factors.OrderByDescending(f => f.Weight).Take(4).Select(f => f.Text).ToArray();
        return new MenuPropensities([.. options.Select(o => o.Id)], probs, key, t);
    }

    /// <summary>21 §7.8 / §8.3: an emotion at or above its hijack threshold pulls probability toward its outlet option.</summary>
    private static void FoldInHijack(SimWorld world, int chooser, IReadOnlyList<PropensityInput> options, IReadOnlyList<FamilyBase> families,
        float[] probs, List<(string, float)> factors)
    {
        ref readonly var e = ref world.People.Emotions[chooser];
        ref readonly var p = ref world.People.Personality[chooser];
        var outlet = -1;
        var pHijack = 0f;
        for (var k = 0; k < options.Count; k++)
        {
            if (options[k].Outlet is not { } emotion) { continue; }
            var fam = families.FirstOrDefault(f => f.Family == options[k].Family);
            if (fam is not null && fam.Includes.Contains(emotion)) { continue; }   // the base already models this emotion
            var value = EmotionValue(e, emotion);
            var theta = Threshold(world.Content, p.Traits, emotion);
            if (value < theta) { continue; }
            var ph = Math.Min(0.9f, MathF.Pow((value - theta) / (100f - theta), 2f) * (1f + (0.3f * DecisionNoise.Z(p.Volatility))));
            if (ph > pHijack || (MathF.Abs(ph - pHijack) < 1e-6f && outlet >= 0 && probs[k] > probs[outlet])) { (pHijack, outlet) = (ph, k); }
        }

        if (outlet < 0 || pHijack <= 0f) { return; }
        for (var k = 0; k < probs.Length; k++) { probs[k] = k == outlet ? pHijack + ((1f - pHijack) * probs[k]) : probs[k] * (1f - pHijack); }
        factors.Add(($"{options[outlet].Id}: {options[outlet].Outlet} boiling over (+{pHijack:0.00})", 2f + pHijack));
    }

    private static float Threshold(ContentDatabase content, ulong traits, string emotion)
    {
        var theta = emotion switch { "anger" or "fear" => 70f, "joy" => 85f, _ => 75f };
        for (var bits = traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < content.Traits.Count && content.Traits[h].Effects?.HijackThreshold is { } ht && ht.TryGetValue(emotion, out var v)) { theta = v; }
        }

        return theta;
    }

    /// <summary>21 §6.3 biases for common option groups (k values from the table).</summary>
    private static float EmotionBias(PropensityInput o, in Emotions e)
    {
        var key = o.UtilityKey ?? o.Family;
        float Up(float emotion, float k) => 1f + (k * emotion / 100f);
        float Down(float emotion, float k) => MathF.Max(0.05f, 1f - (k * emotion / 100f));
        return key switch
        {
            "confront" or "insult" or "assault" or "escalate" => Up(e.Anger, 1.5f) * Down(e.Fear, 0.8f) * Down(e.Joy, 0.5f),
            "refuse" => Up(e.Anger, 1.0f),
            "help" or "gift" or "trade" or "accept" => Down(e.Anger, 0.7f) * Down(e.Jealousy, 0.8f),
            "flee" or "hide" or "de-escalate" => Up(e.Fear, 2.0f),
            "celebrate" or "chat" or "warm" => Up(e.Joy, 1.0f) * Down(e.Shame, 0.8f),
            "withdraw" or "cool" => Up(e.Grief, 1.5f),
            "apologize" => Up(e.Shame, 1.2f),
            _ => 1f,
        };
    }

    private static float ValueAlignment(in ValueBlock v, IReadOnlyDictionary<string, float>? tags)
    {
        if (tags is null) { return 1f; }
        var sum = 0f;
        foreach (var (value, tag) in tags) { sum += tag * (ValueOf(v, value) - 50f) / 50f; }
        return Math.Clamp(1f + (0.6f * sum), 0.4f, 1.8f);
    }

    private static float TraitUtility(ContentDatabase content, ulong traits, string key)
    {
        var mult = 1f;
        for (var bits = traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < content.Traits.Count && content.Traits[h].Effects?.Utility is { } u && u.TryGetValue(key, out var v)) { mult *= v; }
        }

        return mult;
    }

    private static void Note(List<(string, float)> factors, float multiplier, string text)
    {
        if (MathF.Abs(multiplier - 1f) >= 0.05f) { factors.Add((text, MathF.Abs(multiplier - 1f))); }
    }

    private static float EmotionValue(in Emotions e, string emotion) => emotion switch
    {
        "anger" => e.Anger, "fear" => e.Fear, "grief" => e.Grief, "joy" => e.Joy, "shame" => e.Shame, "jealousy" => e.Jealousy, _ => 0f,
    };

    private static byte FacetOf(in Personality p, string facet) => facet switch
    {
        "curiosity" => p.Curiosity, "diligence" => p.Diligence, "sociability" => p.Sociability, "warmth" => p.Warmth, _ => p.Volatility,
    };

    private static byte ValueOf(in ValueBlock v, string value) => value switch
    {
        "family" => v.Family, "wealth" => v.Wealth, "status" => v.Status, "honor" => v.Honor, "tradition" => v.Tradition,
        "faith" => v.Faith, "fairness" => v.Fairness, "freedom" => v.Freedom, _ => v.Loyalty,
    };
}
