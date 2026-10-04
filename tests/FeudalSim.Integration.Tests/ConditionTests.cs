using FeudalSim.Content;
using FeudalSim.Hosting;
using FeudalSim.Sim;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Health;
using FeudalSim.Sim.Systems;
using FeudalSim.Sim.World;

namespace FeudalSim.Integration.Tests;

/// <summary>M2-07a: 11 §2.3, §6.1–6.2, §7.1–7.3 and §8.1 — the condition engine, contagion, water exposure, dehydration and starvation.</summary>
public sealed class ConditionTests
{
    private static readonly ContentDatabase Content = ContentCompiler.Compile(Path.Combine(RepoRoot(), "content")).Database!;

    private static SimWorld Camp(string? water = null)
    {
        var def = ScenarioDef.Load(Path.Combine(RepoRoot(), "content", "scenarios", "m1_camp.yaml"));
        if (water is not null) { def = def with { Camp = (def.Camp ?? new CampDef()) with { WaterSource = water } }; }
        var w = def.CreateWorld(Content, SerialJobScheduler.Instance);
        w.Step();
        return w;
    }

    private long _offset;   // minutes past the clock this test has simulated (xunit: one instance per test)

    /// <summary>Runs one person's health minute by minute; <paramref name="needs"/> sets the needs before each minute.</summary>
    private float Hours(SimWorld w, int row, float hours, Func<float, Needs>? needs = null, Func<bool>? stop = null)
    {
        var minutes = (int)MathF.Round(hours * 60);
        for (var m = 1; m <= minutes; m++)
        {
            w.People.Needs[row] = needs?.Invoke(m / 60f) ?? Needs.Full;
            w.People.Activity[row].Level = ActivityLevel.Rest;
            HealthSystem.Update(w, row, 1f / 60f, w.Clock.GameMinute + ++_offset);
            if (stop?.Invoke() == true) { return m / 60f; }
        }

        return hours;
    }

    private static int AdultRow(SimWorld w)
    {
        for (var i = 0; i < w.People.Count; i++)
        {
            var age = (w.Clock.GameMinute - w.People.Core[i].BirthGameMinute) / Sim.Time.GameDate.MinutesPerYear;
            if (age is >= 18 and < 60) { return i; }
        }

        throw new InvalidOperationException("no adult");
    }

    [Fact]
    public void TheFlux_IncubatesHalfADay_RunsItsCourse_ThenFourDaysOfImmunity()   // 11 §7.2
    {
        var w = Camp();
        var row = AdultRow(w);
        var id = w.People.Ids[row];
        var flux = Content.DiseaseHandle("disease.flux");
        Conditions.Infect(w, row, flux).ShouldBeTrue();
        Conditions.Infect(w, row, flux).ShouldBeFalse();   // already has it
        var start = w.Clock.GameMinute + _offset;

        var onset = Hours(w, row, 20f, stop: () => w.Conditions.Of(id)[0].Stage > 0);
        onset.ShouldBeInRange(8f, 16f);   // incubation 8–16 h
        Conditions.Of(w, id).Fever.ShouldBeTrue();
        Hours(w, row, 12f, stop: () => w.Conditions.Of(id)[0].Stage > 1);
        Conditions.Of(w, id).HydrationMult.ShouldBe(3f);   // acute: Hydration ×3 (11 §2.1)
        Conditions.Of(w, id).LoadPoints.ShouldBe(20f);

        var survived = Hours(w, row, 80f, stop: () => w.Conditions.Of(id).Count == 0 || w.IsDead(row));
        if (w.IsDead(row)) { return; }   // the 2% grave branch: GraveChance covers it
        (onset + 12f + survived).ShouldBeInRange(36f, 88f);   // 8–16 h + 1–2 d of illness + 12–24 h recovering
        var now = start + _offset;
        w.Conditions.Immune(id, flux, now).ShouldBeTrue();
        w.Conditions.Immune(id, flux, now + (4 * 1440) + 1).ShouldBeFalse();
    }

    [Fact]
    public void GraveChances_FollowTheCatalog_WithAgeAndHungerMultipliers()   // 11 §7.2, §8.1
    {
        var w = Camp();
        var adult = AdultRow(w);
        float P(string disease) => Conditions.GraveChance(w, adult, Content.Diseases[Content.DiseaseHandle(disease)]);
        P("disease.flux").ShouldBe(0.02f);
        P("disease.toxin_death_cap").ShouldBe(0.40f);
        P("disease.toxin_hemlock").ShouldBe(0.50f);
        P("disease.toxin_water_hemlock").ShouldBe(0.70f);
        P("disease.toxin_nightshade").ShouldBe(0.15f);
        P("disease.toxin_foxglove").ShouldBe(0.20f);
        P("disease.toxin_lily_of_the_valley").ShouldBe(0.10f);
        P("disease.food_poisoning").ShouldBe(0f);   // ≈ 0 for adults

        w.People.Vitals[adult].Starvation = 55f;
        P("disease.flux").ShouldBe(0.04f);   // malnourished ×2
        w.People.Vitals[adult].Starvation = 80f;
        P("disease.flux").ShouldBe(0.08f);   // starving ×4

        w.People.Core[adult].BirthGameMinute = w.Clock.GameMinute - (8L * Sim.Time.GameDate.MinutesPerYear);
        w.People.Vitals[adult].Starvation = 0f;
        P("disease.flux").ShouldBe(0.06f, 1e-6f);   // children ×3
        P("disease.toxin_nightshade").ShouldBe(0.40f, 0.001f);   // children 40%
        P("disease.food_poisoning").ShouldBe(0.005f);
        w.People.Core[adult].BirthGameMinute = w.Clock.GameMinute - (70L * Sim.Time.GameDate.MinutesPerYear);
        P("disease.flux").ShouldBe(0.05f, 1e-6f);   // elders ×2.5
    }

    [Fact]
    public void WaterHemlock_KillsAboutSevenInTen()   // 11 §8.1: 70% untreated
    {
        var (dead, n) = (0, 0);
        var toxin = Content.DiseaseHandle("disease.toxin_water_hemlock");
        for (var world = 0; world < 6; world++)
        {
            var w = Camp();
            for (var row = 0; row < w.People.Count; row++)
            {
                var age = (w.Clock.GameMinute - w.People.Core[row].BirthGameMinute) / Sim.Time.GameDate.MinutesPerYear;
                Conditions.Infect(w, row, toxin, (ulong)world).ShouldBeTrue();
                var hours = Hours(w, row, 10f, stop: () => w.Conditions.Of(w.People.Ids[row]).Count == 0 || w.IsDead(row));
                hours.ShouldBeLessThanOrEqualTo(8f);   // 0.5–2 h onset, 2–6 h seizures
                n++;
                if (w.IsDead(row)) { dead++; w.People.Vitals[row].Cause.ShouldBe(VitalCause.Disease); }
            }

            w.Step();   // keyed draws differ per world through the clock (Infect salts with the minute)
        }

        n.ShouldBe(144);
        ((float)dead / n).ShouldBeInRange(0.58f, 0.82f);   // 0.70 ± 3σ (σ ≈ 0.038)
    }

    [Fact]
    public void Dehydration_KillsInAboutTwoDays_FromTheDayWithoutWater()   // 11 §2.3, §6.2
    {
        var w = Camp();
        var row = AdultRow(w);
        // Hydration 19 at rest (−3/h): 6.3 h to empty at +2/h, then +5/h — ≈ 23 h from 19 to dead.
        var downed = Hours(w, row, 30f, h => Needs.Full with { Hydration = MathF.Max(0f, 19f - (3f * h)) }, () => w.People.Vitals[row].State >= VitalState.Downed);
        downed.ShouldBeInRange(19f, 23f);
        var dead = downed + Hours(w, row, 10f, h => Needs.Full with { Hydration = 0f }, () => w.IsDead(row));
        dead.ShouldBeInRange(21.5f, 25.5f);
        w.People.Vitals[row].Cause.ShouldBe(VitalCause.Dehydration);
    }

    [Fact]
    public void Dehydration_RecoversTenAnHour_OnceHydrationIsBackAbove40()   // 11 §6.2
    {
        var w = Camp();
        var row = AdultRow(w);
        Hours(w, row, 10f, h => Needs.Full with { Hydration = 0f });
        w.People.Vitals[row].Dehydration.ShouldBe(50f, 0.5f);
        Hours(w, row, 2f, h => Needs.Full with { Hydration = 30f });   // 20–39: no change
        w.People.Vitals[row].Dehydration.ShouldBe(50f, 0.5f);
        Hours(w, row, 3f);
        w.People.Vitals[row].Dehydration.ShouldBe(20f, 0.5f);
    }

    [Fact]
    public void Starvation_FollowsTheFamineCurve_AtRest()   // 11 §6.1: +20/day with no food (≈ 3.5 Sat/h at rest)
    {
        var w = Camp();
        var row = AdultRow(w);
        var empty = Needs.Full with { Satiety = 0f };
        var (underfed, malnourished, starving) = (0f, 0f, 0f);
        var t = 0f;
        while (!w.IsDead(row) && t < 24f * 8)
        {
            t += Hours(w, row, 1f, _ => empty);
            var s = w.People.Vitals[row].Starvation;
            if (underfed == 0f && s >= 25f) { underfed = t; }
            if (malnourished == 0f && s >= 50f) { malnourished = t; }
            if (starving == 0f && s >= 75f) { starving = t; }
        }

        // From Satiety 0 (≈ day 1 of the table): 25 / 50 / 75 / 100 at ≈ 21 S per day.
        (underfed / 24f).ShouldBeInRange(1.0f, 1.4f);
        (malnourished / 24f).ShouldBeInRange(2.1f, 2.6f);
        (starving / 24f).ShouldBeInRange(3.2f, 3.9f);
        (t / 24f).ShouldBeInRange(4.4f, 5.1f);   // ≈ day 6 with the first day's Satiety
        w.People.Vitals[row].Cause.ShouldBe(VitalCause.Starvation);
    }

    [Fact]
    public void Starvation_RecoversSlowly_AndSatietyBurnsFasterWhileRebuilding()   // 11 §6.1
    {
        var w = Camp();
        var row = AdultRow(w);
        w.People.Vitals[row].Starvation = 30f;
        Hours(w, row, 8f);
        w.People.Vitals[row].Starvation.ShouldBe(28f, 0.05f);   // −0.25/h
    }

    [Fact]
    public void A_StreamCamp_DrawsAFewFluxCases_AMarshCamp_Many()   // 11 §11.1: c_src × 0.04 per 0.5 L
    {
        int Cases(string water)
        {
            var w = Camp(water);
            var flux = Content.DiseaseHandle("disease.flux");
            var cases = 0;
            for (var s = 0; s < 3 * 18_000; s++)   // three game days (m1_camp: 750 steps a game hour)
            {
                cases += w.Step().Events.Count(e => e.Payload is ConditionStarted c && c.Disease == flux);
            }

            return cases;
        }

        // 24 people × ≈ 2 L a day × 3 days × p per 0.5 L: stream 0.0008 → ≈ 0.6 cases; marsh 0.012 → ≈ 9 (fewer: the sick are immune for a while).
        Cases("stream").ShouldBeLessThanOrEqualTo(4);
        Cases("marsh").ShouldBeInRange(3, 20);
    }

    [Fact]
    public void TheFlux_SpreadsWithinASleepingRoom_AndNowhereElse()   // 11 §7.3: β 0.005, same room w = (6 / 3)^0.5
    {
        var w = Camp("spring");   // clean water: every case is contact
        var flux = Content.DiseaseHandle("disease.flux");
        w.Camp.ShelterSleeps.ShouldBe((byte)3);
        int[] seeds = [0, 3, 6, 9];   // one per room in rooms 0–3; rooms 4–7 (rows 12–23) have no case
        foreach (var row in seeds)
        {
            Conditions.Infect(w, row, flux).ShouldBeTrue();
            var list = w.Conditions.ListOf(w.People.Ids[row])!;
            list[0] = list[0] with { Stage = 2, StageEndsMin = long.MaxValue / 2 };   // pinned in the acute stage (contagious 1.0)
        }

        var cases = new List<int>();
        for (var s = 0; s < 15 * 18_000; s++)
        {
            foreach (var e in w.Step().Events) { if (e.Payload is ConditionStarted c && c.Disease == flux && !seeds.Contains(w.People.IndexOf(c.Person))) { cases.Add(w.People.IndexOf(c.Person)); } }
        }

        cases.ShouldAllBe(r => r < 12);   // roommates only
        // 8 roommates × (1 − e^(−0.005 × 1.414 × 8 h))^… over 15 nights ≈ 57% each ≈ 4.6.
        cases.Count.ShouldBeInRange(1, 8);
    }

    [Fact]
    public void LiveConditions_AndImmunity_SurviveSaveAndLoad()   // the conditions table (M2-07a)
    {
        var w = Camp();
        var flux = Content.DiseaseHandle("disease.flux");
        Conditions.Infect(w, 2, flux).ShouldBeTrue();
        Conditions.Infect(w, 5, Content.DiseaseHandle("disease.toxin_nightshade")).ShouldBeTrue();
        w.Conditions.Immunize(w.People.Ids[7], flux, w.Clock.GameMinute + 3000);
        for (var s = 0; s < 750 * 10; s++) { w.Step(); }   // into the Flux's course
        var image = Sim.Persistence.SaveCodec.Capture(w);
        for (var s = 0; s < 750 * 20; s++) { w.Step(); }

        var restored = Sim.Persistence.SaveCodec.Restore(image, out var warnings);
        warnings.ShouldBeEmpty();
        restored.Content = Content;
        ScenarioDef.AddCampSystems(restored);
        restored.Conditions.Immune(restored.People.Ids[7], flux, restored.Clock.GameMinute).ShouldBeTrue();
        restored.Conditions.Of(restored.People.Ids[2]).Count.ShouldBe(1);
        for (var s = 0; s < 750 * 20; s++) { restored.Step(); }
        StateHasher.Hash(restored).ShouldBe(StateHasher.Hash(w));
    }

    /// <summary>
    /// Integrates needs decay (the content's 11 §2.1 table) and health minute by minute on an hourly schedule, without water
    /// and/or food; returns the hour of death (or the limit).
    /// </summary>
    private float Deprive(SimWorld w, int row, Func<int, ActivityLevel> schedule, bool water, bool food, float limitH)
    {
        var n = Needs.Full;
        for (var m = 1; m <= limitH * 60; m++)
        {
            var level = schedule((m - 1) / 60 % 24);
            if (!water) { n.Hydration = MathF.Max(0f, n.Hydration - (NeedsDecaySystem.Rate(Content, "need.hydration", level, 0f) / 60f)); }
            if (!food) { n.Satiety = MathF.Max(0f, n.Satiety - (NeedsDecaySystem.Rate(Content, "need.satiety", level, 0f) / 60f)); }
            n.Energy = 80f;
            w.People.Needs[row] = n;
            w.People.Activity[row].Level = level;
            HealthSystem.Update(w, row, 1f / 60f, w.Clock.GameMinute + ++_offset);
            if (w.IsDead(row)) { return m / 60f; }
        }

        return limitH;
    }

    private static ActivityLevel StandardDay(int hour) => hour switch   // 8 h sleep, 2 h rest, 4 h light, 10 h moderate
    {
        < 8 => ActivityLevel.Sleep,
        < 10 => ActivityLevel.Rest,
        < 14 => ActivityLevel.Light,
        _ => ActivityLevel.Moderate,
    };

    [Fact]
    public void T_DEHY_01_NoWater_StandardDay_44To52h_ContinuousModerate_38To44h()   // 11 §23
    {
        var w = Camp();
        var row = AdultRow(w);
        Deprive(w, row, StandardDay, water: false, food: true, 80f).ShouldBeInRange(44f, 52f);
        var w2 = Camp();
        Deprive(w2, row, _ => ActivityLevel.Moderate, water: false, food: true, 80f).ShouldBeInRange(38f, 44f);
        w2.People.Vitals[row].Cause.ShouldBe(VitalCause.Dehydration);
    }

    [Fact]
    public void T_STARVE_01_NoFood_Resting_DiesDay5_5To6_5()   // 11 §23 (the famine table's "none, resting" row; ½ and ¾ rations: M2-07b)
    {
        var w = Camp();
        var row = AdultRow(w);
        var day = Deprive(w, row, h => h < 8 ? ActivityLevel.Sleep : ActivityLevel.Rest, water: true, food: false, 24f * 9) / 24f;
        day.ShouldBeInRange(5.5f, 6.5f);
        w.People.Vitals[row].Cause.ShouldBe(VitalCause.Starvation);
    }

    private static readonly int[] MealTimes = [8 * 60, 18 * 60];

    /// <summary>A standard day on a ration: Satiety decays by the §2.1 table; meals of ration/2 Sat at 08:00 and 18:00.</summary>
    private (float Day, float Starvation) OnRation(SimWorld w, int row, float rationSat, float days)
    {
        var n = Needs.Full;
        for (var m = 1; m <= days * 1440; m++)
        {
            var hour = (m - 1) / 60 % 24;
            var level = StandardDay(hour);
            n.Satiety = MathF.Max(0f, n.Satiety - (NeedsDecaySystem.Rate(Content, "need.satiety", level, 0f) / 60f));
            var minuteOfDay = (m - 1) % 1440;
            if (Array.IndexOf(MealTimes, minuteOfDay) >= 0) { n.Satiety = MathF.Min(100f, n.Satiety + (rationSat / MealTimes.Length)); }
            (n.Hydration, n.Energy) = (100f, 80f);
            w.People.Needs[row] = n;
            w.People.Activity[row].Level = level;
            HealthSystem.Update(w, row, 1f / 60f, w.Clock.GameMinute + ++_offset);
            if (w.IsDead(row)) { return (m / 1440f, w.People.Vitals[row].Starvation); }
        }

        return (days, w.People.Vitals[row].Starvation);
    }

    /// <summary>
    /// T-STARVE-01's ½ and ¾ rows against §6.1's own rule: a full person's Satiety reserve (100) lasts 100 / (95 − intake)
    /// days, then Starvation gains 0.25 × the daily deficit. ½: 2.1 d + 100 / 11.9 ≈ 10.5 d; ¾: 4.2 d, then +5.9/day → S ≈ 22
    /// on day 8. §23's bands (death 8.5–10.5; S 40–55 on day 8) come from the famine table, which leaves the reserve out —
    /// calibration finding 31 D48 (owner decision pending); this test holds the formula, ±7 % for meal timing.
    /// </summary>
    [Fact]
    public void Rations_StarveAsSection6_1Says_ReserveFirstThenAQuarterOfTheDeficit()   // 11 §6.1, §23 T-STARVE-01 (½, ¾; D48)
    {
        var w = Camp();
        var row = AdultRow(w);
        OnRation(w, row, 0.5f * Sim.Survival.Eating.FullRationSat, 14f).Day.ShouldBeInRange(9.8f, 11.2f);
        var w2 = Camp();
        var (day, s) = OnRation(w2, row, 0.75f * Sim.Survival.Eating.FullRationSat, 8f);
        day.ShouldBe(8f);   // alive after 8 days
        s.ShouldBeInRange(18f, 32f);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { dir = dir.Parent; }
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
