using System.IO.Hashing;
using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Social;

public enum FavorKind : byte { Asked, Conditional, Deferred }

/// <summary>
/// A favor someone agreed to do (16 §5.4 request DP): work at a camp task for some game-minutes, now or from a later start
/// (<c>defer</c>). The utility AI gives an active favor's task priority over routine (21: an obligation, P2) unless a need
/// is critical; performing it counts the minutes down. Conditional favors record the asker's return obligation.
/// </summary>
[MessagePackObject]
public sealed class Favor
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public EntityId Doer { get; set; }
    [Key(2)] public EntityId For { get; set; }
    [Key(3)] public short Task { get; set; }
    [Key(4)] public long StartMin { get; set; }
    [Key(5)] public float RemainingMin { get; set; }
    [Key(6)] public FavorKind Kind { get; set; }
}

/// <summary>Agreed favors by id (saved, hashed).</summary>
public sealed class FavorStore
{
    private readonly SortedDictionary<ulong, Favor> _open = [];
    private ulong _lastId;

    public int Count => _open.Count;

    public IEnumerable<Favor> Open => _open.Values;

    /// <summary>The doer's first favor that has started, or null.</summary>
    public Favor? ActiveFor(EntityId doer, long nowMin)
    {
        foreach (var f in _open.Values) { if (f.Doer == doer && f.StartMin <= nowMin) { return f; } }
        return null;
    }

    internal Favor Add(EntityId doer, EntityId forWhom, short task, long startMin, float minutes, FavorKind kind)
    {
        var f = new Favor { Id = ++_lastId, Doer = doer, For = forWhom, Task = task, StartMin = startMin, RemainingMin = minutes, Kind = kind };
        _open.Add(f.Id, f);
        return f;
    }

    internal bool Remove(ulong id) => _open.Remove(id);

    internal (Favor[] Open, ulong LastId) Export() => ([.. _open.Values], _lastId);

    internal void Import(Favor[] open, ulong lastId)
    {
        _open.Clear();
        foreach (var f in open) { _open.Add(f.Id, f); }
        _lastId = lastId;
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, (long)_lastId);
        h.Append(b);
        foreach (var f in _open.Values)
        {
            foreach (var v in (ReadOnlySpan<long>)[(long)f.Id, (long)f.Doer.Value, (long)f.For.Value, f.Task, f.StartMin, BitConverter.SingleToInt32Bits(f.RemainingMin), (long)f.Kind])
            {
                BitConverter.TryWriteBytes(b, v);
                h.Append(b);
            }
        }
    }
}
