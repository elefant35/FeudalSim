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
    private Attributes[] _attributes = new Attributes[64];
    private Personality[] _personality = new Personality[64];
    private Emotions[] _emotions = new Emotions[64];
    private Mood[] _mood = new Mood[64];
    private byte[] _skillLevels = new byte[64 * SkillCount];       // row-major: 28 skills per person, in skill-handle order
    private byte[] _skillAptitude = new byte[64 * SkillCount];     // hidden aptitude × 100 (50–150), 12 §5.4

    /// <summary>Skills per person (canon §10.2); skill handles are content order (ordinal id).</summary>
    public const int SkillCount = 28;

    public int Count { get; private set; }

    public ReadOnlySpan<EntityId> Ids => _ids.AsSpan(0, Count);
    public ReadOnlySpan<string> Names => _names.AsSpan(0, Count);
    public Span<PersonCore> Core => _core.AsSpan(0, Count);
    public Span<Transform> Transforms => _transform.AsSpan(0, Count);
    public Span<Needs> Needs => _needs.AsSpan(0, Count);
    public Span<LodState> Lod => _lod.AsSpan(0, Count);
    public Span<WanderState> Wander => _wander.AsSpan(0, Count);
    public Span<Attributes> Attributes => _attributes.AsSpan(0, Count);
    public Span<Personality> Personality => _personality.AsSpan(0, Count);
    public Span<Emotions> Emotions => _emotions.AsSpan(0, Count);
    public Span<Mood> Mood => _mood.AsSpan(0, Count);

    /// <summary>All rows' skill levels (0–100), row-major, <see cref="SkillCount"/> per row.</summary>
    public Span<byte> SkillLevelsAll => _skillLevels.AsSpan(0, Count * SkillCount);

    /// <summary>All rows' hidden aptitudes × 100, row-major.</summary>
    public Span<byte> SkillAptitudeAll => _skillAptitude.AsSpan(0, Count * SkillCount);

    public Span<byte> SkillLevels(int row) => _skillLevels.AsSpan(row * SkillCount, SkillCount);
    public Span<byte> SkillAptitude(int row) => _skillAptitude.AsSpan(row * SkillCount, SkillCount);

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
        _attributes[i] = default;
        _personality[i] = new Personality { Culture = World.Personality.None, Profession = World.Personality.None };
        _emotions[i] = default;
        _mood[i] = default;
        SkillLevels(i).Clear();
        SkillAptitude(i).Fill(100);
        return i;
    }

    /// <summary>Replaces all rows with the given ids and names (load path); other columns are then filled by the caller.</summary>
    internal void ResetRows(EntityId[] ids, string[] names)
    {
        if (ids.Length != names.Length) { throw new ArgumentException("ids and names differ in length."); }
        for (var i = 1; i < ids.Length; i++)
        {
            if (ids[i].CompareTo(ids[i - 1]) <= 0) { throw new InvalidDataException("Saved ids are not in ascending order."); }
        }

        Count = 0;
        EnsureCapacity(ids.Length);
        Array.Clear(_core); Array.Clear(_transform); Array.Clear(_needs); Array.Clear(_lod); Array.Clear(_wander);
        Array.Clear(_attributes); Array.Clear(_personality); Array.Clear(_emotions); Array.Clear(_mood);
        Array.Clear(_skillLevels); Array.Clear(_skillAptitude);
        ids.CopyTo(_ids, 0);
        names.CopyTo(_names, 0);
        Count = ids.Length;
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
        Array.Resize(ref _attributes, size);
        Array.Resize(ref _personality, size);
        Array.Resize(ref _emotions, size);
        Array.Resize(ref _mood, size);
        Array.Resize(ref _skillLevels, size * SkillCount);
        Array.Resize(ref _skillAptitude, size * SkillCount);
    }
}
