using MessagePack;
using MessagePack.Formatters;

namespace FeudalSim.Sim.Core;

/// <summary>Kinds of runtime entity. Append only: values are persisted inside ids.</summary>
public enum EntityKind : byte
{
    None, Person, Animal, Herd, Household, Settlement, Polity, Faction, Building, Plot,
    Container, ItemInstance, Army, Battle,
}

/// <summary>
/// 64-bit runtime id (canon §14): the top 8 bits encode the kind; ids are never reused (20 §6.3).
/// </summary>
[MessagePackFormatter(typeof(EntityIdFormatter))]
public readonly record struct EntityId(ulong Value) : IComparable<EntityId>
{
    public static readonly EntityId None = default;

    public EntityKind Kind => (EntityKind)(Value >> 56);
    public ulong Serial => Value & 0x00FF_FFFF_FFFF_FFFFUL;
    public bool IsNone => Value == 0;

    public static EntityId Make(EntityKind kind, ulong serial)
    {
        if (serial == 0 || serial > 0x00FF_FFFF_FFFF_FFFFUL)
        {
            throw new ArgumentOutOfRangeException(nameof(serial));
        }

        return new(((ulong)kind << 56) | serial);
    }

    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    public override string ToString() => IsNone ? "none" : $"{Kind}#{Serial}";
}

/// <summary>Allocates never-reused ids per kind. Its counters are part of saved state.</summary>
public sealed class EntityIdAllocator
{
    private readonly ulong[] _next = new ulong[256];

    public EntityId Next(EntityKind kind) => EntityId.Make(kind, ++_next[(int)kind]);

    public ulong Peek(EntityKind kind) => _next[(int)kind];

    public void Restore(EntityKind kind, ulong lastIssued) => _next[(int)kind] = lastIssued;
}

/// <summary>Serializes an <see cref="EntityId"/> as a single unsigned 64-bit integer.</summary>
public sealed class EntityIdFormatter : IMessagePackFormatter<EntityId>
{
    public void Serialize(ref MessagePackWriter writer, EntityId value, MessagePackSerializerOptions options)
        => writer.Write(value.Value);

    public EntityId Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        => new(reader.ReadUInt64());
}
