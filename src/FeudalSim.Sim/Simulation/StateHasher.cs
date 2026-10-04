using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim;

/// <summary>
/// XxHash64 over every table column in canonical (id) order plus the clock and allocators
/// (20 §8.7). The single hash function used by determinism tests, save/load checks and replays.
/// </summary>
public static class StateHasher
{
    public static ulong Hash(SimWorld world)
    {
        var h = new XxHash64();
        Span<byte> scalars = stackalloc byte[32];
        BitConverter.TryWriteBytes(scalars[0..], world.WorldSeed);
        BitConverter.TryWriteBytes(scalars[8..], world.Clock.Step);
        BitConverter.TryWriteBytes(scalars[16..], world.Clock.GameMs);
        BitConverter.TryWriteBytes(scalars[24..], world.Clock.DayLengthMinutes);
        h.Append(scalars);

        var people = world.People;
        h.Append(MemoryMarshal.AsBytes(people.Ids));
        foreach (var name in people.Names)
        {
            h.Append(MemoryMarshal.AsBytes(name.AsSpan()));
        }

        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<PersonCore>)people.Core));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<Transform>)people.Transforms));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<Needs>)people.Needs));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<LodState>)people.Lod));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<WanderState>)people.Wander));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<Attributes>)people.Attributes));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<Personality>)people.Personality));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<Emotions>)people.Emotions));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<Mood>)people.Mood));
        h.Append(MemoryMarshal.AsBytes((ReadOnlySpan<ActivityState>)people.Activity));
        h.Append(MemoryMarshal.AsBytes(new ReadOnlySpan<CampRecord>(in world.Camp)));
        h.Append(MemoryMarshal.AsBytes(new ReadOnlySpan<PlayerState>(in world.Player)));
        h.Append(people.SkillLevelsAll);
        h.Append(people.SkillAptitudeAll);
        world.Decisions.HashInto(h);
        world.HashAiInto(h);
        world.Conversations.HashInto(h);
        world.Confrontations.HashInto(h);
        world.Relationships.HashInto(h);
        world.Memories.HashInto(h);
        world.Claims.HashInto(h);
        world.Beliefs.HashInto(h);
        world.Reputation.HashInto(h);
        return h.GetCurrentHashAsUInt64();
    }
}
