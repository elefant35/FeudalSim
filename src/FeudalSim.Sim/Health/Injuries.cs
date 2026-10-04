using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Health;

/// <summary>11 §4.1 body regions (18's <c>BodyRegion</c>).</summary>
public enum BodyRegion : byte { Head, Torso, ArmL, ArmR, LegL, LegR }

/// <summary>11 §4.2 injury types (byte values saved; append only).</summary>
public enum InjuryType : byte { Cut, Puncture, Bruise, Fracture, Burn, Frostbite, Concussion, Internal }

/// <summary>18 §2.4 damage types that reach 11 as trauma.</summary>
public enum DamageType : byte { Cut, Pierce, Blunt, Burn }

/// <summary>11 §5.3 contamination sources (c₀): clean blade 0.2 · tool/wood/earth/arrow 0.5 · animal 0.8 · burn 0.3.</summary>
public enum TraumaSource : byte { CleanBlade, Tool, Animal, Fire }

/// <summary>11 §5.3 infection track (M2-06b drives it; M2-06a creates wounds Clean).</summary>
public enum InfectionState : byte { Clean, Inflamed, Infected, Septic }

/// <summary>11 §14 states (byte values saved; append only).</summary>
public enum VitalState : byte { Active, Impaired, Downed, Dying, Recovering, Dead }

/// <summary>Why someone went down or died (11 §14 triggers).</summary>
public enum VitalCause : byte { None, Trauma, BloodLoss, Hypothermia }

/// <summary>11 §4.2 injury record (blittable, packed without padding: saved and hashed as bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Injury
{
    public const byte OpenFlag = 1, ArterialFlag = 2;

    public ulong Id;
    public long CreatedMin;
    public float Severity, BleedRate, Contamination, InfectionSev, TreatQuality;
    public BodyRegion Region;
    public InjuryType Type;
    public InfectionState Infection;
    public byte Flags;
    public ushort Treated, ClotHours;   // treatment flags (06b); whole hours of clotting applied so far

    public readonly bool Open => (Flags & OpenFlag) != 0;
    public readonly bool Arterial => (Flags & ArterialFlag) != 0;
}

/// <summary>11 §4.1 per-person vitals: Blood, the Bruise pool, Pain and the derived Health, plus the §14 state machine.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Vitals
{
    public float Blood, Bruise, Pain, Health;
    public long DownedSinceMin, StableSinceMin;
    public VitalState State;
    public VitalCause Cause;
    public byte Reserved0, Reserved1;

    public static Vitals Healthy => new() { Blood = 100f, Health = 100f, DownedSinceMin = -1, StableSinceMin = -1 };

    public readonly bool Down => State is VitalState.Downed or VitalState.Dying or VitalState.Recovering;

    public readonly bool Dead => State == VitalState.Dead;
}

/// <summary>Injury records by person (ascending id), saved (<c>injuries</c>) and hashed. Healed injuries are removed.</summary>
public sealed class InjuryStore
{
    private readonly SortedDictionary<ulong, List<Injury>> _byPerson = [];
    private ulong _lastId;

    public int Count => _byPerson.Values.Sum(l => l.Count);

    public IReadOnlyList<Injury> Of(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? l : [];

    internal List<Injury>? ListOf(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? l : null;

    internal ulong NextId() => ++_lastId;

    internal void Add(EntityId person, Injury injury)
    {
        if (!_byPerson.TryGetValue(person.Value, out var list)) { _byPerson[person.Value] = list = []; }
        list.Add(injury);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct Row
    {
        public ulong Owner;
        public Injury Injury;
    }

    internal (Row[] Rows, ulong LastId) Export() => ([.. _byPerson.SelectMany(kv => kv.Value.Select(i => new Row { Owner = kv.Key, Injury = i }))], _lastId);

    internal void Import(Row[] rows, ulong lastId)
    {
        _byPerson.Clear();
        foreach (var r in rows)
        {
            if (!_byPerson.TryGetValue(r.Owner, out var list)) { _byPerson[r.Owner] = list = []; }
            list.Add(r.Injury);
        }

        _lastId = lastId;
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, _lastId);
        h.Append(b);
        foreach (var (person, list) in _byPerson)
        {
            BitConverter.TryWriteBytes(b, person);
            h.Append(b);
            h.Append(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(list)));
        }
    }
}
