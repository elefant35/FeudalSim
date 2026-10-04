using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.World;

/// <summary>
/// Generates a person's attributes, personality and skills (M1-01) as a pure function of (world seed, person id,
/// culture, profession, age) on the <see cref="RngStream.PersonGen"/> stream. Rules: attributes 12 §3.2; facets and
/// values canon §10.4 / 21 §4.2; traits 21 §4.4; skills from a homeland trade 12 §8.5; aptitude 12 §5.4.
/// The draw order below is part of the format: changing it changes every generated person.
/// </summary>
public static class PersonGenerator
{
    public const int AdultAge = 14;

    private static readonly string[] CommonSkills = ["skill.athletics", "skill.cooking", "skill.farming", "skill.foraging", "skill.woodcutting"];

    /// <summary>Fills row <paramref name="row"/>'s personality, attributes and skills; returns the age in years.</summary>
    /// <param name="culture">Culture handle, or <see cref="Personality.None"/>.</param>
    /// <param name="profession">Profession handle, or <see cref="Personality.None"/> to draw a homeland trade (adults only).</param>
    /// <param name="ageYears">Age, or 0 to draw an adult age (18–50).</param>
    public static int Generate(SimWorld world, int row, ushort culture, ushort profession, int ageYears)
    {
        var content = world.Content;
        var people = world.People;
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.PersonGen, people.Ids[row].Value, 0, Salt.PersonGen));
        var age = ageYears > 0 ? ageYears : 18 + rng.Range(0, 33);

        // Attributes: Potential N(5, 1.4) clamped 2–8 (Training, AgeMod and the rest arrive with 12's systems).
        people.Attributes[row] = new Attributes
        {
            Strength = Potential(ref rng), Endurance = Potential(ref rng), Dexterity = Potential(ref rng),
            Perception = Potential(ref rng), Intellect = Potential(ref rng), Charisma = Potential(ref rng),
        };

        // Facets N(50, 15); values = culture mean (default 50) + N(0, 15); both clamped 0–100.
        var cultureDef = culture < content.Cultures.Count ? content.Cultures[culture] : null;
        var p = new Personality
        {
            Curiosity = Facet(ref rng), Diligence = Facet(ref rng), Sociability = Facet(ref rng), Warmth = Facet(ref rng), Volatility = Facet(ref rng),
            Culture = culture, Profession = Personality.None,
        };
        Span<int> values = stackalloc int[9];
        for (var v = 0; v < 9; v++)
        {
            var mean = cultureDef?.ValueMeans is { } means && means.TryGetValue(CanonLists.Values[v], out var m) ? m : 50;
            values[v] = Clamp100(mean + rng.Normal(0f, 15f));
        }

        p.Traits = DrawTraits(content, p, cultureDef, ref rng);
        ApplyTraitValueRules(content, p.Traits, values);
        p.Values = new ValueBlock
        {
            Family = (byte)values[0], Wealth = (byte)values[1], Status = (byte)values[2], Honor = (byte)values[3], Tradition = (byte)values[4],
            Faith = (byte)values[5], Fairness = (byte)values[6], Freedom = (byte)values[7], Loyalty = (byte)values[8],
        };

        // Profession: a homeland trade (first era ≤ 1) for adults when none was given.
        var adult = age >= AdultAge;
        if (adult && profession == Personality.None && content.Professions.Count > 0)
        {
            var trades = Enumerable.Range(0, content.Professions.Count).Where(i => content.Professions[i].FirstEra <= 1).ToArray();
            profession = (ushort)trades[rng.Range(0, trades.Length)];
        }

        p.Profession = adult ? profession : Personality.None;
        people.Personality[row] = p;
        SeedSkills(content, people, row, adult ? p.Profession : Personality.None, age, adult, ref rng);
        people.Emotions[row] = new Emotions { UpdatedGameMs = world.Clock.GameMs };
        people.Mood[row] = default;
        return age;
    }

    private static ulong DrawTraits(ContentDatabase content, in Personality p, CultureDef? culture, ref Rng rng)
    {
        var traits = content.Traits;
        if (traits.Count == 0) { return 0; }
        var r = rng.NextFloat01();
        var count = r < 0.35f ? 2 : r < 0.80f ? 3 : 4;   // canon: 2–4 at 35 / 45 / 20 %
        Span<float> z = [Z(p.Curiosity), Z(p.Diligence), Z(p.Sociability), Z(p.Warmth), Z(p.Volatility)];
        Span<float> weight = stackalloc float[traits.Count];
        ulong chosen = 0;
        for (var pick = 0; pick < count; pick++)
        {
            var total = 0f;
            for (var t = 0; t < traits.Count; t++)
            {
                weight[t] = 0f;
                if ((chosen & (1UL << t)) != 0 || Excluded(content, traits[t], chosen)) { continue; }
                var affinity = 0f;
                if (traits[t].FacetAffinity is { } aff)
                {
                    for (var f = 0; f < 5; f++) { if (aff.TryGetValue(CanonLists.Facets[f], out var k)) { affinity += k * z[f]; } }
                }

                var cultureMult = culture?.TraitMultipliers is { } cm && cm.TryGetValue(traits[t].Id, out var c) ? c : 1f;
                weight[t] = traits[t].Prevalence * SimMath.Exp(affinity) * cultureMult;
                total += weight[t];
            }

            if (total <= 0f) { break; }
            var x = rng.NextFloat01() * total;
            var picked = -1;
            for (var t = 0; t < traits.Count; t++)
            {
                if (weight[t] <= 0f) { continue; }
                picked = t;   // the last positive weight absorbs float rounding at the top end
                x -= weight[t];
                if (x < 0f) { break; }
            }

            chosen |= 1UL << picked;
        }

        return chosen;
    }

    private static bool Excluded(ContentDatabase content, TraitDef candidate, ulong chosen)
    {
        if (candidate.Incompatible is null) { return false; }
        foreach (var other in candidate.Incompatible)
        {
            var h = content.TraitHandle(other);
            if (h >= 0 && (chosen & (1UL << h)) != 0) { return true; }
        }

        return false;
    }

    /// <summary>Trait value rules (e.g. Pious: Faith ≥ 70; Skeptic: Faith ≤ 30; Ascetic: Faith +10) — shift, then floor, then cap.</summary>
    private static void ApplyTraitValueRules(ContentDatabase content, ulong traits, Span<int> values)
    {
        for (var t = 0; t < content.Traits.Count; t++)
        {
            if ((traits & (1UL << t)) == 0 || content.Traits[t].Effects is not { } e) { continue; }
            for (var v = 0; v < 9; v++)
            {
                var name = CanonLists.Values[v];
                if (e.ValueShift?.TryGetValue(name, out var shift) == true) { values[v] = Math.Clamp(values[v] + shift, 0, 100); }
                if (e.ValueFloor?.TryGetValue(name, out var floor) == true) { values[v] = Math.Max(values[v], floor); }
                if (e.ValueCap?.TryGetValue(name, out var cap) == true) { values[v] = Math.Min(values[v], cap); }
            }
        }
    }

    /// <summary>12 §8.5 (adults) and the children's rule; aptitude per 12 §5.4. Needs the 28 canonical skills.</summary>
    private static void SeedSkills(ContentDatabase content, PersonTable people, int row, ushort profession, int age, bool adult, ref Rng rng)
    {
        var levels = people.SkillLevels(row);
        var aptitude = people.SkillAptitude(row);
        if (content.Skills.Count != PersonTable.SkillCount) { return; }

        Span<float> domainBias = [rng.Normal(0f, 0.1f), rng.Normal(0f, 0.1f), rng.Normal(0f, 0.1f), rng.Normal(0f, 0.1f)];
        for (var s = 0; s < PersonTable.SkillCount; s++)
        {
            var apt = Math.Clamp(1f + domainBias[(int)content.Skills[s].Domain] + rng.Normal(0f, 0.17f), 0.5f, 1.5f);
            aptitude[s] = (byte)MathF.Round(apt * 100f);
        }

        var common = CommonSkills.Select(content.SkillHandle).ToArray();
        if (!adult)
        {
            levels.Clear();
            foreach (var s in common) { levels[s] = (byte)rng.Range(0, 11); }   // children: common skills 0–10, nothing else
            return;
        }

        for (var s = 0; s < PersonTable.SkillCount; s++) { levels[s] = (byte)rng.Range(0, 9); }                // everything else U(0, 8)
        foreach (var s in common) { levels[s] = (byte)Math.Max(levels[s], rng.Range(5, 21)); }                     // common U(5, 20) unless higher
        if (profession == Personality.None || profession >= content.Professions.Count) { return; }

        var trade = content.Professions[profession];
        var primary = trade.Primary.Select(content.SkillHandle).ToArray();
        var secondary = (trade.Secondary ?? []).Select(content.SkillHandle).Where(s => !primary.Contains(s)).Take(2).ToList();
        while (secondary.Count < 2)
        {
            var s = rng.Range(0, PersonTable.SkillCount);
            if (!primary.Contains(s) && !secondary.Contains(s)) { secondary.Add(s); }
        }

        foreach (var s in secondary) { levels[s] = (byte)Math.Max(levels[s], rng.Range(10, 31)); }                // 2 secondary U(10, 30)
        foreach (var s in primary)
        {
            levels[s] = (byte)Math.Clamp((int)MathF.Round(22f + (0.9f * (age - 18)) + rng.Normal(0f, 6f)), 15, 65);   // primary
        }
    }

    private static float Potential(ref Rng rng) => Math.Clamp(rng.Normal(5f, 1.4f), 2f, 8f);

    private static byte Facet(ref Rng rng) => (byte)Clamp100(rng.Normal(50f, 15f));

    private static int Clamp100(float v) => Math.Clamp((int)MathF.Round(v), 0, 100);

    private static float Z(byte facet) => (facet - 50f) / 15f;

    /// <summary>Sex from a full name's given name (1 = female if it is in the culture's <c>female_names</c>; else 0).</summary>
    public static byte SexOf(Content.ContentDatabase content, int culture, string name)
    {
        var given = name.Split(' ', 2)[0];
        var def = culture >= 0 && culture < content.Cultures.Count ? content.Cultures[culture] : null;
        return def?.FemaleNames is { } female && female.Contains(given) ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// M1-30: a name for an unnamed spawn — given + family name from the culture's lists, keyed on the world seed and the
    /// person's id (deterministic, order-independent of anything else); a full name already in use is redrawn (≤ 32 tries),
    /// then numbered. Cultures without lists name people "Settler N".
    /// </summary>
    public static string Name(SimWorld world, EntityId id, int culture, int ordinal)
    {
        var def = culture >= 0 && culture < world.Content.Cultures.Count ? world.Content.Cultures[culture] : null;
        if (def?.GivenNames is not { Count: > 0 } given || def.FamilyNames is not { Count: > 0 } family) { return $"Settler {ordinal}"; }
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.PersonGen, id.Value, Salt.PersonName, 0));
        var people = world.People;
        bool Taken(string n)
        {
            for (var i = 0; i < people.Count; i++) { if (string.Equals(people.Names[i], n, StringComparison.Ordinal)) { return true; } }
            return false;
        }

        string name = "";
        for (var tries = 0; tries < 32; tries++)
        {
            name = $"{given[rng.Range(0, given.Count)]} {family[rng.Range(0, family.Count)]}";
            if (!Taken(name)) { return name; }
        }

        return $"{name} {ordinal}";
    }
}
