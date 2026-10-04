using System.IO.Hashing;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;

namespace FeudalSim.Sim.World;

/// <summary>
/// M2-08: things put down on the ground — a pile is a container (its goods live in the inventory store like anyone's
/// carry) with a place. Dropping within 1.5 m of a pile adds to it; an emptied pile disappears. Felled timber lands in a
/// pile at the stump (nobody carries three logs: 11 §12.1). Saved and hashed.
/// </summary>
public sealed class PileStore
{
    public const float MergeM = 1.5f, ReachM = 3f;

    [MessagePack.MessagePackObject]
    public struct Pile
    {
        [MessagePack.Key(0)] public float X;
        [MessagePack.Key(1)] public float Z;
        [MessagePack.Key(2)] public long CreatedMin;
    }

    private readonly SortedDictionary<ulong, Pile> _piles = [];

    public int Count => _piles.Count;

    public IEnumerable<KeyValuePair<ulong, Pile>> All => _piles;

    public bool TryGet(EntityId id, out Pile pile) => _piles.TryGetValue(id.Value, out pile);

    /// <summary>The pile at (x, z): an existing one within <see cref="MergeM"/> (the first by id), or a new one.</summary>
    public EntityId At(SimWorld world, float x, float z)
    {
        foreach (var (id, p) in _piles)
        {
            if (((p.X - x) * (p.X - x)) + ((p.Z - z) * (p.Z - z)) <= MergeM * MergeM) { return new EntityId(id); }
        }

        var nid = world.Ids.Next(EntityKind.Container);
        _piles[nid.Value] = new Pile { X = x, Z = z, CreatedMin = world.Clock.GameMinute };
        return nid;
    }

    /// <summary>Forgets piles whose goods are gone.</summary>
    public void Prune(SimWorld world)
    {
        List<ulong>? gone = null;
        foreach (var id in _piles.Keys) { if (world.Inventory.Of(new EntityId(id)).Count == 0) { (gone ??= []).Add(id); } }
        foreach (var id in gone ?? []) { _piles.Remove(id); }
    }

    [MessagePack.MessagePackObject]
    public sealed class Snapshot
    {
        [MessagePack.Key(0)] public ulong[] Ids { get; set; } = [];
        [MessagePack.Key(1)] public Pile[] Piles { get; set; } = [];
    }

    public Snapshot Export() => new() { Ids = [.. _piles.Keys], Piles = [.. _piles.Values] };

    public void Import(Snapshot s)
    {
        _piles.Clear();
        for (var k = 0; k < s.Ids.Length; k++) { _piles[s.Ids[k]] = s.Piles[k]; }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[24];
        foreach (var (id, p) in _piles)
        {
            BitConverter.TryWriteBytes(b, id);
            BitConverter.TryWriteBytes(b[8..], p.X);
            BitConverter.TryWriteBytes(b[12..], p.Z);
            BitConverter.TryWriteBytes(b[16..], p.CreatedMin);
            h.Append(b);
        }
    }

    /// <summary>Puts <paramref name="qty"/> of what the person sees as <paramref name="seen"/> down where they stand. Why not, or null.</summary>
    public static string? Drop(SimWorld world, int row, int seen, int qty)
    {
        if (!world.CanAct(row)) { return "can't now"; }
        var who = world.People.Ids[row];
        if (seen < 0 || world.Inventory.CountSeen(who, seen) < qty || qty <= 0) { return "you don't have that many"; }
        ref readonly var t = ref world.People.Transforms[row];
        var pile = world.Piles.At(world, t.X, t.Z);
        world.Inventory.MoveSeen(who, pile, seen, qty);
        world.Emit(Salience.Minor, who, new ItemsMoved(who, pile, seen, qty));
        return null;
    }

    /// <summary>Picks <paramref name="qty"/> of <paramref name="seen"/> up from a pile within reach. Why not, or null.</summary>
    public static string? PickUp(SimWorld world, int row, EntityId pile, int seen, int qty)
    {
        if (!world.CanAct(row)) { return "can't now"; }
        if (!world.Piles.TryGet(pile, out var p)) { return "there's nothing there"; }
        ref readonly var t = ref world.People.Transforms[row];
        if (((t.X - p.X) * (t.X - p.X)) + ((t.Z - p.Z) * (t.Z - p.Z)) > ReachM * ReachM) { return "out of reach"; }
        qty = Math.Min(qty, world.Inventory.CountSeen(pile, seen));
        if (seen < 0 || qty <= 0) { return "there's none of that there"; }
        var who = world.People.Ids[row];
        world.Inventory.MoveSeen(pile, who, seen, qty);
        world.Piles.Prune(world);
        world.Emit(Salience.Minor, who, new ItemsMoved(pile, who, seen, qty));
        return null;
    }
}
