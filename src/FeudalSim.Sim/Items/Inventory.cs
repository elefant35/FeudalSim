using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Items;

/// <summary>20 §6.5 inventory slot: a commodity stack (Instance 0, Qty, mean Q) or one unique item (Qty 1, Instance id).
/// <c>Label</c> is what its holders believe it is (11 §8.2; −1 = what it truly is).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Slot
{
    public ulong Instance;
    public int Item, Qty, Label;
    public byte Q, Reserved0;

    /// <summary>11 §10.4 spoilage of a food stack: 0 fresh … 65535 rotten (freshness F = 1 − Spoil/65535). Old saves read 0.</summary>
    public ushort Spoil;

    public readonly float Freshness => 1f - (Spoil / 65535f);

    /// <summary>The item as its holders see it.</summary>
    public readonly int Seen => Label >= 0 ? Label : Item;
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

    /// <summary>How many of an item the container holds as its holders see it (labels, 11 §8.2).</summary>
    public int CountSeen(EntityId container, int seen)
    {
        if (!_byContainer.TryGetValue(container.Value, out var list)) { return 0; }
        var n = 0;
        foreach (var s in list) { if (s.Seen == seen) { n += s.Qty; } }
        return n;
    }

    /// <summary>Relabels the commodity stacks of <paramref name="item"/> labelled <paramref name="label"/> (−1 = true); merges back if needed.</summary>
    public int Relabel(EntityId container, int item, int label, int newLabel)
    {
        var at = Find(container.Value, item, 0, label);
        if (at < 0) { return 0; }
        var list = _byContainer[container.Value];
        var s = list[at];
        list.RemoveAt(at);
        if (list.Count == 0) { _byContainer.Remove(container.Value); }
        Add(container, item, s.Qty, s.Q, newLabel);
        return s.Qty;
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

    /// <summary>Adds a commodity quantity at quality <paramref name="q"/> (a faucet: gathering, harvest, salvage, scenarios),
    /// believed to be <paramref name="label"/> (−1: known for what it is). Stacks merge only with the same label.</summary>
    public void Add(EntityId container, int item, int qty, int q = 50, int label = -1, ushort spoil = 0)
    {
        if (qty <= 0) { return; }
        if (label == item) { label = -1; }
        var list = ListFor(container.Value);
        var at = Find(container.Value, item, 0, label);
        if (at >= 0)
        {
            var s = list[at];
            s.Q = MergeQ(s.Q, s.Qty, q, qty);
            s.Spoil = (ushort)Math.Round(((s.Spoil * (double)s.Qty) + (spoil * (double)qty)) / (s.Qty + qty), MidpointRounding.AwayFromZero);   // mixed stacks average
            s.Qty += qty;
            list[at] = s;
            return;
        }

        Insert(list, new Slot { Item = item, Qty = qty, Q = (byte)Math.Clamp(q, 0, 100), Label = label, Spoil = spoil });
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
        Insert(ListFor(container.Value), new Slot { Item = item, Qty = 1, Instance = id, Q = inst.Q, Label = -1 });
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

    /// <summary>
    /// Takes one unit from the first commodity stack its holders see as <paramref name="seen"/> (11 §8.2: a mislabelled
    /// stack gives up what it truly is). False if there is none.
    /// </summary>
    public bool TakeOneSeen(EntityId container, int seen, out int item, out int q) => TakeOneSeen(container, seen, out item, out q, out _);

    public bool TakeOneSeen(EntityId container, int seen, out int item, out int q, out float freshness)
    {
        (item, q, freshness) = (-1, 0, 1f);
        if (!_byContainer.TryGetValue(container.Value, out var list)) { return false; }
        for (var k = 0; k < list.Count; k++)
        {
            var s = list[k];
            if (s.Instance != 0 || s.Seen != seen) { continue; }
            (item, q, freshness) = (s.Item, s.Q, s.Freshness);
            if (--s.Qty == 0) { list.RemoveAt(k); } else { list[k] = s; }
            if (list.Count == 0) { _byContainer.Remove(container.Value); }
            return true;
        }

        return false;
    }

    /// <summary>Moves a quantity of what the holders see as <paramref name="seen"/> (labels kept: 11 §8.2), stacks first, then instances.</summary>
    public void MoveSeen(EntityId from, EntityId to, int seen, int qty)
    {
        if (!_byContainer.TryGetValue(from.Value, out var list)) { return; }
        for (var k = 0; k < list.Count && qty > 0;)
        {
            var s = list[k];
            if (s.Seen != seen) { k++; continue; }
            if (s.Instance != 0)
            {
                list.RemoveAt(k);
                Insert(ListFor(to.Value), s);
                qty--;
                continue;
            }

            var n = Math.Min(qty, s.Qty);
            s.Qty -= n;
            qty -= n;
            if (s.Qty == 0) { list.RemoveAt(k); } else { list[k] = s; k++; }
            Add(to, s.Item, n, s.Q, s.Label, s.Spoil);
        }

        if (list.Count == 0) { _byContainer.Remove(from.Value); }
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
        for (var stack = Find(from, item, 0); stack >= 0 && qty > 0; stack = Find(from, item, 0))   // stacks first, any label
        {
            var s = list[stack];
            var n = Math.Min(qty, s.Qty);
            s.Qty -= n;
            qty -= n;
            if (s.Qty == 0) { list.RemoveAt(stack); } else { list[stack] = s; }
            if (to is { } dest) { Add(new EntityId(dest), item, n, s.Q, s.Label, s.Spoil); }
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

    /// <summary>Destroys one instance a holder carries (a tool worn out, an item eaten or burnt): a sink.</summary>
    internal bool Destroy(EntityId holder, ulong instance)
    {
        if (!_byContainer.TryGetValue(holder.Value, out var list)) { return false; }
        var at = list.FindIndex(s => s.Instance == instance);
        if (at < 0) { return false; }
        list.RemoveAt(at);
        if (list.Count == 0) { _byContainer.Remove(holder.Value); }
        return _instances.Remove(instance);
    }

    /// <summary>Updates an instance (wear, repair, flaws found); its slot's Q follows.</summary>
    internal void Update(in ItemInstance instance) => _instances[instance.Id] = instance;

    private List<Slot> ListFor(ulong container)
    {
        if (!_byContainer.TryGetValue(container, out var list)) { _byContainer[container] = list = []; }
        return list;
    }

    /// <summary>The slot of (item, instance); with <paramref name="label"/> given, only that label (−1 = true); else any label.</summary>
    private int Find(ulong container, int item, ulong instance, int? label = null)
    {
        if (!_byContainer.TryGetValue(container, out var list)) { return -1; }
        for (var k = 0; k < list.Count; k++)
        {
            if (list[k].Item == item && list[k].Instance == instance && (label is null || list[k].Label == label)) { return k; }
        }

        return -1;
    }

    private static void Insert(List<Slot> list, Slot slot)
    {
        var at = 0;
        while (at < list.Count && (list[at].Item < slot.Item || (list[at].Item == slot.Item && (list[at].Instance < slot.Instance
            || (list[at].Instance == slot.Instance && list[at].Label < slot.Label))))) { at++; }
        list.Insert(at, slot);
    }

    /// <summary>
    /// 11 §10.4: ages every food stack by <paramref name="hours"/> at temperature factor <paramref name="tempF"/>
    /// (dF/day = tempF / shelf life); stacks that reach F = 0 rot away and are reported in <paramref name="rotted"/>.
    /// Containers in id order, slots in order: deterministic.
    /// </summary>
    public void AgeFood(ContentDatabase content, float tempF, float hours, List<(ulong Container, int Item, int Qty)> rotted)
    {
        rotted.Clear();
        List<ulong>? empty = null;
        foreach (var (container, list) in _byContainer)
        {
            for (var k = list.Count - 1; k >= 0; k--)
            {
                var s = list[k];
                if (s.Instance != 0 || content.Items[s.Item].Food is not { } food || food.ShelfDays <= 0f) { continue; }
                var add = 65535.0 * tempF * hours / (food.ShelfDays * 24.0);
                var spoil = s.Spoil + (int)Math.Round(add, MidpointRounding.AwayFromZero);
                if (spoil >= 65535)
                {
                    rotted.Add((container, s.Item, s.Qty));
                    list.RemoveAt(k);
                    continue;
                }

                s.Spoil = (ushort)spoil;
                list[k] = s;
            }

            if (list.Count == 0) { (empty ??= []).Add(container); }
        }

        foreach (var c in empty ?? []) { _byContainer.Remove(c); }
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
