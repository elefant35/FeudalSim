using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Items;

/// <summary>20 §6.5 inventory slot: a commodity stack (Instance 0, Qty, mean Q) or one unique item (Qty 1, Instance id).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Slot
{
    public ulong Instance;
    public int Item, Qty;
    public byte Q, Reserved0, Reserved1, Reserved2;
}

/// <summary>13 §5 item instance: quality, flaws (a mask over flaw handles), durability and provenance (13 §5.9).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ItemInstance
{
    public ulong Id, Flaws, Maker;
    public long MadeMin;
    public int Item;
    public float Condition, MaxDurability;
    public short Recipe;
    public byte Q, Marked;

    public readonly bool Has(int flaw) => flaw is >= 0 and < 64 && (Flaws & (1UL << flaw)) != 0;
}

/// <summary>
/// 20 §6.5 InventoryStore (M2-09): slots per container (a person's id is their own container; camps, buildings and
/// chests get <c>Container</c> ids), kept sorted by (item, instance) so every walk is deterministic; and the
/// <c>ItemInstance</c> table. Stacks merge with a quantity-weighted mean Q (13 §5.9). Quantities change only through
/// <see cref="Add"/>, <see cref="Create"/> and <see cref="Remove"/>; <see cref="Move"/> conserves them. Saved and hashed.
/// </summary>
public sealed class InventoryStore
{
    private readonly SortedDictionary<ulong, List<Slot>> _byContainer = [];
    private readonly SortedDictionary<ulong, ItemInstance> _instances = [];

    public int Containers => _byContainer.Count;

    public int InstanceCount => _instances.Count;

    public IReadOnlyList<Slot> Of(EntityId container) => _byContainer.TryGetValue(container.Value, out var l) ? l : [];

    public ItemInstance? Instance(ulong id) => _instances.TryGetValue(id, out var i) ? i : null;

    /// <summary>How many of an item the container holds (stack quantity plus instances).</summary>
    public int Count(EntityId container, int item)
    {
        if (!_byContainer.TryGetValue(container.Value, out var list)) { return 0; }
        var n = 0;
        foreach (var s in list) { if (s.Item == item) { n += s.Qty; } }
        return n;
    }

    /// <summary>The commodity stack's mean Q, or −1 if there is none.</summary>
    public int StackQ(EntityId container, int item)
    {
        var at = Find(container.Value, item, 0);
        return at < 0 ? -1 : _byContainer[container.Value][at].Q;
    }

    /// <summary>Carried mass in kg (11 §12.1 encumbrance reads it).</summary>
    public float Mass(EntityId container, ContentDatabase content)
    {
        if (!_byContainer.TryGetValue(container.Value, out var list)) { return 0f; }
        var kg = 0.0;
        foreach (var s in list) { kg += s.Qty * content.Items[s.Item].MassKg; }
        return (float)kg;
    }

    /// <summary>Adds a commodity quantity at quality <paramref name="q"/> (a faucet: gathering, harvest, salvage, scenarios).</summary>
    public void Add(EntityId container, int item, int qty, int q = 50)
    {
        if (qty <= 0) { return; }
        var list = ListFor(container.Value);
        var at = Find(container.Value, item, 0);
        if (at >= 0)
        {
            var s = list[at];
            s.Q = MergeQ(s.Q, s.Qty, q, qty);
            s.Qty += qty;
            list[at] = s;
            return;
        }

        Insert(list, new Slot { Item = item, Qty = qty, Q = (byte)Math.Clamp(q, 0, 100) });
    }

    /// <summary>13 §5.9: merging stacks averages Q by quantity (rounded half away from zero — never banker's rounding).</summary>
    public static byte MergeQ(int q1, int n1, int q2, int n2)
        => (byte)Math.Clamp((int)Math.Round(((q1 * (double)n1) + (q2 * (double)n2)) / (n1 + n2), MidpointRounding.AwayFromZero), 0, 100);

    /// <summary>Creates a unique item (a faucet: crafting, salvage, scenarios) in a container; returns its id.</summary>
    public ulong Create(EntityId container, int item, int q, float maxDurability, ulong id, ulong maker = 0, long madeMin = 0, short recipe = -1, ulong flaws = 0)
    {
        var inst = new ItemInstance
        {
            Id = id, Item = item, Q = (byte)Math.Clamp(q, 0, 100), Flaws = flaws, Maker = maker, MadeMin = madeMin, Recipe = recipe,
            MaxDurability = maxDurability, Condition = maxDurability,
        };
        _instances[id] = inst;
        Insert(ListFor(container.Value), new Slot { Item = item, Qty = 1, Instance = id, Q = inst.Q });
        return id;
    }

    /// <summary>Removes <paramref name="qty"/> of an item (a sink: eating, burning, breaking): the stack first, then instances
    /// lowest id first. False (and nothing removed) if the container holds fewer.</summary>
    public bool Remove(EntityId container, int item, int qty)
    {
        if (qty <= 0 || Count(container, item) < qty) { return false; }
        Take(container.Value, item, qty, null);
        return true;
    }

    /// <summary>Moves a quantity between containers, conserving it (the stack keeps its Q; instances move whole).</summary>
    public bool Move(EntityId from, EntityId to, int item, int qty)
    {
        if (qty <= 0 || from == to || Count(from, item) < qty) { return false; }
        Take(from.Value, item, qty, to.Value);
        return true;
    }

    private void Take(ulong from, int item, int qty, ulong? to)
    {
        var list = _byContainer[from];
        var stack = Find(from, item, 0);
        if (stack >= 0)
        {
            var s = list[stack];
            var n = Math.Min(qty, s.Qty);
            s.Qty -= n;
            qty -= n;
            if (s.Qty == 0) { list.RemoveAt(stack); } else { list[stack] = s; }
            if (to is { } dest) { Add(new EntityId(dest), item, n, s.Q); }
        }

        while (qty > 0)
        {
            var at = list.FindIndex(x => x.Item == item && x.Instance != 0);
            var slot = list[at];
            list.RemoveAt(at);
            qty--;
            if (to is { } dest) { Insert(ListFor(dest), slot); } else { _instances.Remove(slot.Instance); }
        }

        if (list.Count == 0) { _byContainer.Remove(from); }
    }

    /// <summary>Updates an instance (wear, repair, flaws found); its slot's Q follows.</summary>
    internal void Update(in ItemInstance instance) => _instances[instance.Id] = instance;

    private List<Slot> ListFor(ulong container)
    {
        if (!_byContainer.TryGetValue(container, out var list)) { _byContainer[container] = list = []; }
        return list;
    }

    private int Find(ulong container, int item, ulong instance)
    {
        if (!_byContainer.TryGetValue(container, out var list)) { return -1; }
        for (var k = 0; k < list.Count; k++) { if (list[k].Item == item && list[k].Instance == instance) { return k; } }
        return -1;
    }

    private static void Insert(List<Slot> list, Slot slot)
    {
        var at = 0;
        while (at < list.Count && (list[at].Item < slot.Item || (list[at].Item == slot.Item && list[at].Instance < slot.Instance))) { at++; }
        list.Insert(at, slot);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Row
    {
        public ulong Container;
        public Slot Slot;
    }

    internal (Row[] Slots, ItemInstance[] Instances) Export()
        => ([.. _byContainer.SelectMany(kv => kv.Value.Select(s => new Row { Container = kv.Key, Slot = s }))], [.. _instances.Values]);

    internal void Import(Row[] slots, ItemInstance[] instances)
    {
        _byContainer.Clear();
        _instances.Clear();
        foreach (var r in slots) { ListFor(r.Container).Add(r.Slot); }
        foreach (var i in instances) { _instances[i.Id] = i; }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var (container, list) in _byContainer)
        {
            BitConverter.TryWriteBytes(b, container);
            h.Append(b);
            h.Append(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)));
        }

        foreach (var inst in _instances.Values) { h.Append(MemoryMarshal.AsBytes(new ReadOnlySpan<ItemInstance>(in inst))); }
    }
}
