using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Sim.Crafting;

/// <summary>
/// 13 §9.5 foraging and 11 §8.2 misidentification (M2-13). A gathering trip works one plant or bush node in season,
/// within 3 m: it yields the node's produce and leaves the node harvested until next spring (a node delta). Where an
/// edible patch has its poisonous look-alike growing within 30 m — or the gatherer is picking the poisonous one — an ID
/// check decides: p_correct = clamp(0.6 + 0.004·Foraging + 0.02·(PER − 5) − confusability, 0.5, 0.995) (lore know-how
/// and true beliefs join with 12 §8 and 16). A failure brings home the toxin under the edible's label. Anyone inspecting a
/// stack later (the cook, the healer) re-checks with their own skill and corrects the label on success.
/// </summary>
public static class Foraging
{
    public const byte NodeHarvested = 3;
    public const float LookalikeRangeM = 30f, ReachM = 3f, TripMinutes = 20f;

    /// <summary>11 §8.2 chance to tell an edible from its look-alike.</summary>
    public static float CorrectId(float foraging, float perception, float confusability)
        => Math.Clamp(0.6f + (0.004f * foraging) + (0.02f * (perception - 5f)) - confusability, 0.5f, 0.995f);

    internal static void Command(SimWorld world, in Commands.CommandEnvelope command, Commands.Forage c)
    {
        var people = world.People;
        var row = people.IndexOf(c.Worker);
        if (row < 0 || (command.Source == Commands.CommandSource.Player && row != world.PlayerRow)) { world.RejectCommand(command, "Forage: the player gathers as themselves."); return; }
        if (Gather(world, row, c.Chunk, c.Index) is { } why) { world.RejectCommand(command, $"Forage: {why}."); }
    }

    public static string? Gather(SimWorld world, int row, int chunk, int index)
    {
        if (!world.CanAct(row)) { return "can't act"; }
        if (world.Map is not { } map) { return "no world here"; }
        var content = world.Content;
        var per = NodeScatter.ChunksPerSide(map.Grid);
        if (chunk < 0 || chunk >= per * per || index < 0) { return "no such plant"; }
        var nodes = new List<ResourceNode>();
        NodeScatter.Chunk(map.Grid, map.AttemptSeed, world.NodeTable, chunk % per, chunk / per, nodes);
        world.NodeDeltas.Apply(chunk, nodes);
        if (index >= nodes.Count) { return "no such plant"; }
        var node = nodes[index];
        var def = content.Nodes[node.Type];
        if (def.Forage is not { } yield) { return "nothing to gather there"; }
        if (node.State != 0) { return "already picked over"; }
        if (def.Seasons.Count > 0 && !def.Seasons.Contains(Time.GameDate.FromGameMinute(world.Clock.GameMinute).Season.ToString().ToLowerInvariant())) { return "out of season"; }
        var (x, z) = NodeScatter.Position(map.Grid, chunk % per, chunk / per, node);
        ref readonly var t = ref world.People.Transforms[row];
        if (((t.X - x) * (t.X - x)) + ((t.Z - z) * (t.Z - z)) > ReachM * ReachM) { return "out of reach"; }

        var item = content.ItemHandle(yield.Item);
        var label = -1;
        var who = world.People.Ids[row];
        var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Crafting, who.Value, ((ulong)(uint)chunk << 20) | (uint)index, Salt.ForageId));
        var skill = content.SkillHandle("skill.foraging");
        var foraging = skill >= 0 ? world.People.SkillLevels(row)[skill] : 0;
        var per5 = Skills.Skills.Attribute(world, row, "per");
        if (def.Toxic)
        {
            // Picking the poisonous one: is it taken for the edible it resembles?
            var edible = EdibleLike(content, node.Type);
            if (edible >= 0 && content.Nodes[edible].Forage is { } ef && !rng.Chance(CorrectId(foraging, per5, content.Nodes[edible].ConfusableRisk))) { label = content.ItemHandle(ef.Item); }
        }
        else if (def.Confusable is { } lookId && content.NodeHandle(lookId) is var look and >= 0 && content.Nodes[look].Forage is { } lf
                 && Nearby(world, map, chunk, x, z, look) && !rng.Chance(CorrectId(foraging, per5, def.ConfusableRisk)))
        {
            (item, label) = (content.ItemHandle(lf.Item), item);   // the look-alike came home as the edible (11 §8.2)
        }

        world.Inventory.Add(who, item, yield.Qty, 50, label);
        world.NodeDeltas.Set(chunk, index, NodeHarvested);
        if (skill >= 0) { Skills.Skills.AwardXp(world, row, skill, 10f * TripMinutes / 60f, 15f, Skills.Outcome.Success, TripMinutes / 60f); }
        world.Emit(Salience.Minor, who, new Foraged(who, chunk, index, item, label >= 0 ? label : item, yield.Qty));
        return null;
    }

    /// <summary>The edible node whose look-alike is <paramref name="toxic"/> (−1 if none).</summary>
    private static int EdibleLike(ContentDatabase content, int toxic)
    {
        var id = content.Nodes[toxic].Id;
        for (var k = 0; k < content.Nodes.Count; k++) { if (content.Nodes[k].Confusable == id) { return k; } }
        return -1;
    }

    /// <summary>Whether a node of <paramref name="type"/> stands within 30 m (same and neighbouring chunks).</summary>
    private static bool Nearby(SimWorld world, World.WorldMap map, int chunk, float x, float z, int type)
    {
        var per = NodeScatter.ChunksPerSide(map.Grid);
        int cx0 = chunk % per, cz0 = chunk / per;
        var nodes = new List<ResourceNode>();
        for (var dz = -1; dz <= 1; dz++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                int cx = cx0 + dx, cz = cz0 + dz;
                if (cx < 0 || cz < 0 || cx >= per || cz >= per) { continue; }
                NodeScatter.Chunk(map.Grid, map.AttemptSeed, world.NodeTable, cx, cz, nodes);
                foreach (var n in nodes)
                {
                    if (n.Type != type) { continue; }
                    var (nx, nz) = NodeScatter.Position(map.Grid, cx, cz, n);
                    if (((nx - x) * (nx - x)) + ((nz - z) * (nz - z)) <= LookalikeRangeM * LookalikeRangeM) { return true; }
                }
            }
        }

        return false;
    }

    /// <summary>11 §8.2 second chances: an inspector re-checks the stacks a container holds under one label.</summary>
    internal static void Inspect(SimWorld world, in Commands.CommandEnvelope command, Commands.InspectItem c)
    {
        var people = world.People;
        var row = people.IndexOf(c.Inspector);
        var holder = people.IndexOf(c.Container);
        if (row < 0 || (command.Source == Commands.CommandSource.Player && row != world.PlayerRow) || !world.CanAct(row)) { world.RejectCommand(command, "InspectItem: can't inspect."); return; }
        if (holder >= 0 && holder != row)
        {
            ref readonly var a = ref people.Transforms[row];
            ref readonly var b = ref people.Transforms[holder];
            if (((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)) > 2.5f * 2.5f) { world.RejectCommand(command, "InspectItem: out of reach."); return; }
        }

        var content = world.Content;
        var seen = content.ItemHandle(c.Item);
        var skill = content.SkillHandle("skill.foraging");
        var foraging = skill >= 0 ? people.SkillLevels(row)[skill] : 0;
        var per = Skills.Skills.Attribute(world, row, "per");
        var risk = content.Nodes.FirstOrDefault(n => n.Forage?.Item == c.Item && n.Confusable is not null)?.ConfusableRisk ?? 0.15f;
        var corrected = 0;
        foreach (var slot in world.Inventory.Of(c.Container).Where(s => s.Label == seen && s.Instance == 0).ToList())
        {
            var rng = new Rng(SplitMix64.Mix(world.WorldSeed, (ulong)RngStream.Crafting, c.Inspector.Value, (ulong)slot.Item, ((ulong)world.Clock.Step << 8) | Salt.ForageId));
            if (rng.Chance(CorrectId(foraging, per, risk))) { corrected += world.Inventory.Relabel(c.Container, slot.Item, slot.Label, -1); }
        }

        world.Emit(Salience.Minor, c.Inspector, new ItemInspected(c.Inspector, c.Container, seen, corrected));
    }
}
