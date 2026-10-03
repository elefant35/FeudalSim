using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.World;

/// <summary>
/// The Person table: dense, id-ordered columns (20 §6.2). Rows are appended in ascending
/// <see cref="EntityId"/> order, so table order is canonical order for iteration and hashing.
/// </summary>
public sealed class PersonTable
{
    private EntityId[] _ids = new EntityId[64];
    private string[] _names = new string[64];
    private PersonCore[] _core = new PersonCore[64];
    private Transform[] _transform = new Transform[64];
    private Needs[] _needs = new Needs[64];
    private LodState[] _lod = new LodState[64];
    private WanderState[] _wander = new WanderState[64];

    public int Count { get; private set; }

    public ReadOnlySpan<EntityId> Ids => _ids.AsSpan(0, Count);
    public ReadOnlySpan<string> Names => _names.AsSpan(0, Count);
    public Span<PersonCore> Core => _core.AsSpan(0, Count);
    public Span<Transform> Transforms => _transform.AsSpan(0, Count);
    public Span<Needs> Needs => _needs.AsSpan(0, Count);
    public Span<LodState> Lod => _lod.AsSpan(0, Count);
    public Span<WanderState> Wander => _wander.AsSpan(0, Count);

    public int Add(EntityId id, string name, in PersonCore core, in Transform transform, in Needs needs)
    {
        if (id.Kind != EntityKind.Person) { throw new ArgumentException("Not a person id.", nameof(id)); }
        if (Count > 0 && id.CompareTo(_ids[Count - 1]) <= 0)
        {
            throw new InvalidOperationException("Rows must be appended in ascending id order.");
        }

        EnsureCapacity(Count + 1);
        var i = Count++;
        _ids[i] = id;
        _names[i] = name;
        _core[i] = core;
        _transform[i] = transform;
        _needs[i] = needs;
        _lod[i] = new LodState { Tier = LodTier.Lod1 };
        _wander[i] = new WanderState { HomeX = transform.X, HomeZ = transform.Z };
        return i;
    }

    /// <summary>Row index of an id, or −1. Binary search over the id-ordered column.</summary>
    public int IndexOf(EntityId id) => Math.Max(-1, Array.BinarySearch(_ids, 0, Count, id));

    private void EnsureCapacity(int needed)
    {
        if (needed <= _ids.Length) { return; }
        var size = Math.Max(needed, _ids.Length * 2);
        Array.Resize(ref _ids, size);
        Array.Resize(ref _names, size);
        Array.Resize(ref _core, size);
        Array.Resize(ref _transform, size);
        Array.Resize(ref _needs, size);
        Array.Resize(ref _lod, size);
        Array.Resize(ref _wander, size);
    }
}
