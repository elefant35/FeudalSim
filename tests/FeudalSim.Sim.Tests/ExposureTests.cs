using FeudalSim.Sim.Climate;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Survival;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Tests;

/// <summary>M2-05a: 11 §9 exposure against the doc's own anchors (§2.3, §3.2, §9.1–9.4).</summary>
public sealed class ExposureTests
{
    // 11 §9.2 homeland kit: shirt, tunic, hose, shoes, cloak, hood (Ins 12.5).
    private static readonly WearValues[] Kit = [new(1, 0.2f, 0), new(4, 0.6f, 0.1f), new(2, 0.6f, 0.1f), new(1, 0.4f, 0.3f), new(3, 0.6f, 0.3f), new(1.5f, 0.6f, 0.2f)];

    private static Worn Dressed() => new() { Under = 0, Torso = 1, Legs = 2, Feet = 3, Cloak = 4, Head = 5, Hands = -1, Reserved = -1 };

    private static ExposureInputs Open(float airC, float wind, ActivityLevel level, Sky sky = Sky.Clear, float fireDist = float.MaxValue)
        => new(airC, wind, sky, 0f, 0f, 0f, fireDist, level, 0f, false);

    [Fact]
    public void The_homeland_kit_is_Ins_12_5_dry_and_6_9_soaked()
    {
        Exposure.Insulation(Dressed(), Kit, 0f).ShouldBe(12.5f, 0.001f);
        Exposure.Insulation(Dressed(), Kit, 100f).ShouldBe(6.9f, 0.001f);
        Exposure.Insulation(Worn.None, Kit, 0f).ShouldBe(0f);
        Exposure.RainResist(Dressed(), Kit).ShouldBe(0.3f);
    }

    [Fact]
    public void Naked_at_zero_on_open_ground_loses_warmth_80_to_0_in_about_six_hours()   // 11 §2.3
    {
        var c = Exposure.CoreC(Open(0f, 2f, ActivityLevel.Rest), 0f);
        c.ShouldBe(0f);
        var (w, hours) = (80f, 0f);
        while (w > 0f) { w = Exposure.StepWarmth(w, c, false, false, 0.01f); hours += 0.01f; }
        hours.ShouldBe(5.7f, 0.1f);
        Exposure.StepHypothermia(0f, 0f, c, 1f).ShouldBe(18.5f, 0.001f);   // §9.4: naked at 0 °C, +18.5/h
    }

    [Fact]
    public void Hypothermia_reaches_100_about_five_hours_after_warmth_runs_out()   // 11 §2.3: 4–5 h
    {
        var (w, h, hours) = (0f, 0f, 0f);
        while (h < 100f) { h = Exposure.StepHypothermia(h, w, 0f, 0.01f); hours += 0.01f; }
        hours.ShouldBeInRange(4f, 5.5f);
        Exposure.StepHypothermia(60f, 45f, 20f, 1f).ShouldBe(45f);   // recovers 15/h once Warmth ≥ 40
        Exposure.StepHypothermia(0f, 15f, 10f, 1f).ShouldBe(4f);    // Freezing: (25 − W)/2.5
    }

    [Fact]
    public void Warmth_dynamics_follow_the_three_bands()
    {
        Exposure.StepWarmth(50f, 20f, false, false, 1f).ShouldBe(60f);    // C ≥ 18: +10/h toward 100
        Exposure.StepWarmth(50f, 5f, true, false, 1f).ShouldBe(60f);      // within 3 m of a fire: +10/h
        Exposure.StepWarmth(90f, 15f, false, false, 1f).ShouldBe(84f);    // 14–18: toward 80 at 6/h
        Exposure.StepWarmth(78f, 15f, false, false, 1f).ShouldBe(80f);
        Exposure.StepWarmth(50f, 10f, false, true, 1f).ShouldBe(45f);     // elders and children ×1.25
    }

    [Fact]
    public void Wind_chill_fire_and_activity_follow_the_formula()
    {
        Exposure.WindChill(7f, 0f).ShouldBe(3f, 0.001f);
        Exposure.WindChill(30f, 0f).ShouldBe(8f);                         // capped
        Exposure.WindChill(7f, 0.8f).ShouldBe(0.6f, 0.001f);              // sailcloth shelter
        (Exposure.FireBonus(1f), Exposure.FireBonus(2.5f), Exposure.FireBonus(3.5f), Exposure.FireBonus(6f)).ShouldBe((10f, 6f, 3f, 0f));
        Exposure.ActivityHeat(ActivityLevel.Heavy).ShouldBe(7f);
    }

    [Fact]
    public void Landfall_predawn_on_the_beach_in_the_kit_by_the_fire_is_comfortable()
    {
        // 10 §6.2's −2.0 °C at 04:00, wind 3, sitting 2 m from the fire, dry kit: C = −2 − 0.6 + 6 + 12.5 = 15.9 → warms toward 80.
        var x = Open(-2f, 3f, ActivityLevel.Rest, fireDist: 2f);
        Exposure.CoreC(x, 12.5f).ShouldBe(15.9f, 0.01f);
        // The same night in the sailcloth shelter, asleep on a bough bed (+3): C = −2 + 2 − 0.12 + 12.5 + 3 − 2 = 13.38 (slowly chilling).
        var shelter = new ExposureInputs(-2f, 3f, Sky.Clear, 0.8f, 0.9f, 2f, float.MaxValue, ActivityLevel.Sleep, 3f, false);
        Exposure.CoreC(shelter, 12.5f).ShouldBe(13.38f, 0.01f);
    }

    [Fact]
    public void Clothed_swimmer_in_8C_spring_sea_loses_about_20_warmth_an_hour()   // 11 §9.1
    {
        var c = Exposure.ImmersionC(8f, Exposure.Insulation(Dressed(), Kit, 100f), ActivityLevel.Moderate);
        (Exposure.ImmersionLossFactor * (14f - c)).ShouldBe(20f, 0.5f);
    }

    [Fact]
    public void Surf_soaked_wool_dries_by_a_campfire_in_about_three_hours()   // 11 §9.3
    {
        var x = Open(2.5f, 3f, ActivityLevel.Rest, fireDist: 2f);
        var (wet, hours) = (100f, 0f);
        while (wet > 0f) { wet = Exposure.StepWetness(wet, x, 0.3f, 15f, 0.01f); hours += 0.01f; }
        hours.ShouldBeInRange(3f, 4f);
    }

    [Fact]
    public void Rain_wets_through_the_cloak_and_a_good_shelter_keeps_it_off()
    {
        Exposure.StepWetness(0f, Open(8f, 6f, ActivityLevel.Moderate, Sky.Rain), 0.3f, 15f, 1f).ShouldBe(14f, 0.001f);   // 20 × (1 − 0.3)
        Exposure.StepWetness(0f, Open(8f, 6f, ActivityLevel.Heavy, Sky.Clear), 0.3f, 25f, 0.1f).ShouldBe(0f);           // sweat < drying
        var sheltered = new ExposureInputs(8f, 6f, Sky.Rain, 0.8f, 0.9f, 2f, float.MaxValue, ActivityLevel.Sleep, 3f, false);
        Exposure.StepWetness(50f, sheltered, 0.3f, 15f, 1f).ShouldBeLessThan(50f);                                     // out of the rain: drying
    }

    [Fact]
    public void Sleep_on_bare_ground_at_warmth_45_restores_about_30_energy_in_8_hours()   // 11 §3.2
    {
        (8.5f * 0.55f * Exposure.SleepWarmthFactor(45f) * 8f).ShouldBe(29.9f, 0.1f);
        (Exposure.SleepWarmthFactor(70f), Exposure.SleepWarmthFactor(30f), Exposure.SleepWarmthFactor(10f)).ShouldBe((1f, 0.5f, 0.2f));
    }

    [Fact]
    public void Soaked_kit_asleep_from_dusk_at_1C_is_lethal_to_the_inert()
    {
        // 11 §2.3 (finding 11 Q7): by the formulas the sleeper is Freezing near 22:00 (doc: ≈ 02:00) and Hypothermia ≈ 74 at
        // dawn (doc: ≈ 50) — harsher than the prose, same conclusion: survivable only if they wake and move.
        var x = Open(1f, 3f, ActivityLevel.Sleep);
        var (w, wet, h, t) = (62f, 100f, 0f, 0f);
        var freezingAt = -1f;
        while (t < 13.5f)
        {
            var c = Exposure.CoreC(x, Exposure.Insulation(Dressed(), Kit, wet));
            wet = Exposure.StepWetness(wet, x, 0.3f, c, 0.01f);
            w = Exposure.StepWarmth(w, c, false, false, 0.01f);
            h = Exposure.StepHypothermia(h, w, c, 0.01f);
            if (freezingAt < 0f && w < 25f) { freezingAt = t; }
            t += 0.01f;
        }

        (17.25f + freezingAt).ShouldBeInRange(21f, 26f);   // 21:00–02:00
        h.ShouldBeInRange(50f, 90f);
    }
}
