using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Survival;

/// <summary>
/// 11 §10 eating (M2-07b, part i). Someone eats one unit of what they carry, chosen by what they believe it is; what it
/// truly is decides the rest: Satiety (× the raw penalty, since M2 has no cooking yet, × a sick gut's absorption,
/// capped at 100 with the surplus wasted), hydration, the 6-day nutrition shares (§10.3), a toxin's dose (§8.1) and raw
/// food's poisoning chance. The camp's communal meals count as provisions (ship's biscuit and salt pork: staple 0.6,
/// protein 0.4). Spoilage (§10.4) and rationing (§10.5) are part ii.
/// </summary>
public static class Eating
{
    /// <summary>The 6-day time constant of the nutrition shares (11 §10.3 "rolling 6-day share"), in game minutes.</summary>
    public const float DietTauMin = 6f * 1440f;

    /// <summary>A group counts toward variety at ≥ 15 % of recent Satiety (11 §10.3).</summary>
    public const float GroupShare = 0.15f;

    public static void Command(SimWorld world, in CommandEnvelope command, Eat c)
    {
        var row = world.People.IndexOf(c.Eater);
        if (row < 0 || (command.Source == CommandSource.Player && row != world.PlayerRow)) { world.RejectCommand(command, "Eat: the player eats as themselves."); return; }
        if (Feed(world, row, world.Content.ItemHandle(c.Item)) is { } why) { world.RejectCommand(command, $"Eat: {why}."); }
    }

    /// <summary>Eats one unit of <paramref name="seen"/> from the person's own carry; returns why not, or null.</summary>
    public static string? Feed(SimWorld world, int row, int seen)
    {
        if (!world.CanAct(row)) { return "can't eat now"; }
        var content = world.Content;
        if (seen < 0 || content.Items[seen].Food is null) { return "that isn't food"; }
        var people = world.People;
        var who = people.Ids[row];
        var ravenous = people.Needs[row].Satiety < 20f;
        if (FreshestSeen(world, who, seen) is not { } f) { return "you have none"; }
        if (f <= SpoiledF && !ravenous && content.Items[seen].Food is { Flesh: false }) { return "it has spoiled (only the starving would eat it)"; }   // 11 §10.4
        if (!world.Inventory.TakeOneSeen(who, seen, out var item, out _, out var freshness)) { return "you have none"; }

        var def = content.Items[item];
        var now = world.Clock.GameMinute;
        var sat = 0f;
        if (def.Food is { } food)
        {
            var absorb = Health.Conditions.Of(world, who).SatietyAbsorb;
            ref var n = ref people.Needs[row];
            // 11 §10.4: stale ×0.9; spoiled flesh full value but likely poisonous; spoiled other food ×0.5 (eaten only when Ravenous).
            var (fresh, poison) = freshness > StaleF ? (1f, 0f) : freshness > SpoiledF ? (0.9f, food.Flesh ? 0.02f : 0f) : food.Flesh ? (1f, 0.35f) : (0.5f, 0.15f);
            sat = MathF.Min(100f - n.Satiety, food.Sat * food.RawMult * fresh * absorb);
            n.Satiety += sat;
            n.Hydration = MathF.Min(100f, n.Hydration + food.Hyd);
            Record(world, row, food.Sat * food.RawMult * food.Groups.GetValueOrDefault("staple"), food.Sat * food.RawMult * food.Groups.GetValueOrDefault("protein"),
                food.Sat * food.RawMult * food.Groups.GetValueOrDefault("fresh"));
            var p = 1f - ((1f - food.RawPoisonP) * (1f - poison));
            if (p > 0f)
            {
                var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Health, who.Value, (ulong)now, Salt.Eat ^ ((ulong)(uint)item << 8)));
                if (rng.Chance(p)) { Health.Conditions.Infect(world, row, content.DiseaseHandle("disease.food_poisoning"), (ulong)now); }
            }
        }

        if (def.Toxin is { } toxin) { Health.Conditions.Poison(world, row, content.DiseaseHandle(toxin), def.ToxinDose, (ulong)now); }
        world.Emit(Salience.Minor, who, new Ate(who, item, seen, sat));
        return null;
    }

    /// <summary>Adds Satiety eaten by group to the person's rolling shares (decayed to now first).</summary>
    public static void Record(SimWorld world, int row, float staple, float protein, float fresh)
    {
        ref var d = ref world.People.Diet[row];
        var now = world.Clock.GameMinute;
        var keep = d.AtMin == 0 ? 0f : SimMath.Exp(-(now - d.AtMin) / DietTauMin);
        (d.Staple, d.Protein, d.Fresh, d.AtMin) = ((d.Staple * keep) + staple, (d.Protein * keep) + protein, (d.Fresh * keep) + fresh, now);
    }

    /// <summary>11 §10.4 freshness bands: fresh above 0.6, stale to 0.2, spoiled below.</summary>
    public const float StaleF = 0.6f, SpoiledF = 0.2f;

    /// <summary>The freshness of the first stack the person would eat from (as <see cref="InventoryStore.TakeOneSeen"/> picks), or null.</summary>
    private static float? FreshestSeen(SimWorld world, Core.EntityId who, int seen)
    {
        foreach (var s in world.Inventory.Of(who)) { if (s.Instance == 0 && s.Seen == seen) { return s.Freshness; } }
        return null;
    }

    /// <summary>11 §10.1: a full ration is 95 Sat a day (one adult's moderate day).</summary>
    public const float FullRationSat = 95f;

    /// <summary>
    /// 11 §10.5: what the person may still draw from the camp's store today under its ration (∞ at a full ration).
    /// </summary>
    public static float StoreLeft(SimWorld world, int row)
    {
        var pct = world.Camp.RationPct;
        if (pct is 0 or >= 100) { return float.MaxValue; }
        ref readonly var d = ref world.People.Diet[row];
        var today = (int)(world.Clock.GameMinute / 1440);
        return MathF.Max(0f, (FullRationSat * pct / 100f) - (d.StoreDay == today ? d.StoreSatToday : 0f));
    }

    /// <summary>Counts Satiety drawn from the store today.</summary>
    public static void CountStore(SimWorld world, int row, float sat)
    {
        ref var d = ref world.People.Diet[row];
        var today = (int)(world.Clock.GameMinute / 1440);
        if (d.StoreDay != today) { (d.StoreDay, d.StoreSatToday) = (today, 0f); }
        d.StoreSatToday += sat;
    }

    /// <summary>How many of the three groups make up ≥ 15 % of recent eating (3 varied, 2 plain, 1 monotonous; 0 = nothing eaten yet).</summary>
    public static int Groups(in Diet d)
    {
        var total = d.Staple + d.Protein + d.Fresh;
        if (total <= 0f) { return 0; }
        return (d.Staple / total >= GroupShare ? 1 : 0) + (d.Protein / total >= GroupShare ? 1 : 0) + (d.Fresh / total >= GroupShare ? 1 : 0);
    }
}
