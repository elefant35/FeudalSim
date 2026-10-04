using System.IO.Hashing;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.World;

public enum TileKnowledge : byte { Unknown, HeardOf, Seen }

/// <summary>
/// 10 §10 personal map knowledge (M2-01c-ii): per person, 2 bits per 128 m tile of the 8,192 m region (64 × 64 = 1 KB),
/// states unknown · heard of · seen. Tiles within sight become seen as a person moves; telling and maps add "heard of".
/// Feature knowledge (a deposit, a ford, a den) is a belief (16), not a tile. Saved and hashed.
/// </summary>
public sealed class KnowledgeStore
{
    public const int TileM = 128, Tiles = 64, Bytes = Tiles * Tiles / 4;
    public const float RegionHalfM = 4096f;
    private readonly SortedDictionary<ulong, byte[]> _byPerson = [];

    public static (int Tx, int Tz) TileOf(float x, float z)
        => (Math.Clamp((int)MathF.Floor((x + RegionHalfM) / TileM), 0, Tiles - 1), Math.Clamp((int)MathF.Floor((z + RegionHalfM) / TileM), 0, Tiles - 1));

    public TileKnowledge Get(EntityId person, int tx, int tz)
    {
        if (!_byPerson.TryGetValue(person.Value, out var bits)) { return TileKnowledge.Unknown; }
        var k = (tz * Tiles) + tx;
        return (TileKnowledge)((bits[k >> 2] >> ((k & 3) * 2)) & 3);
    }

    /// <summary>Raises a tile's state (never lowers it: seen stays seen).</summary>
    public bool Raise(EntityId person, int tx, int tz, TileKnowledge state)
    {
        if (tx < 0 || tz < 0 || tx >= Tiles || tz >= Tiles) { return false; }
        if (!_byPerson.TryGetValue(person.Value, out var bits)) { _byPerson[person.Value] = bits = new byte[Bytes]; }
        var k = (tz * Tiles) + tx;
        var shift = (k & 3) * 2;
        var current = (bits[k >> 2] >> shift) & 3;
        if ((int)state <= current) { return false; }
        bits[k >> 2] = (byte)((bits[k >> 2] & ~(3 << shift)) | ((int)state << shift));
        return true;
    }

    /// <summary>Marks every tile whose centre lies within <paramref name="radiusM"/> as seen; returns how many changed.</summary>
    public int See(EntityId person, float x, float z, float radiusM)
    {
        var changed = 0;
        var (t0x, t0z) = TileOf(x - radiusM, z - radiusM);
        var (t1x, t1z) = TileOf(x + radiusM, z + radiusM);
        for (var tz = t0z; tz <= t1z; tz++)
        {
            for (var tx = t0x; tx <= t1x; tx++)
            {
                var cx = (tx * TileM) - RegionHalfM + (TileM / 2f);
                var cz = (tz * TileM) - RegionHalfM + (TileM / 2f);
                var dx = MathF.Max(0f, MathF.Abs(cx - x) - (TileM / 2f));
                var dz = MathF.Max(0f, MathF.Abs(cz - z) - (TileM / 2f));
                if ((dx * dx) + (dz * dz) <= radiusM * radiusM && Raise(person, tx, tz, TileKnowledge.Seen)) { changed++; }
            }
        }

        return changed;
    }

    public int Count(EntityId person, TileKnowledge state)
    {
        if (!_byPerson.TryGetValue(person.Value, out var bits)) { return state == TileKnowledge.Unknown ? Tiles * Tiles : 0; }
        var n = 0;
        for (var k = 0; k < Tiles * Tiles; k++) { if (((bits[k >> 2] >> ((k & 3) * 2)) & 3) == (int)state) { n++; } }
        return n;
    }

    internal (ulong[] People, byte[] Bits) Export()
    {
        var people = _byPerson.Keys.ToArray();
        var bits = new byte[people.Length * Bytes];
        for (var p = 0; p < people.Length; p++) { Array.Copy(_byPerson[people[p]], 0, bits, p * Bytes, Bytes); }
        return (people, bits);
    }

    internal void Import(ulong[] people, byte[] bits)
    {
        _byPerson.Clear();
        for (var p = 0; p < people.Length; p++) { _byPerson[people[p]] = bits.AsSpan(p * Bytes, Bytes).ToArray(); }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var (person, bits) in _byPerson)
        {
            BitConverter.TryWriteBytes(b, person);
            h.Append(b);
            h.Append(bits);
        }
    }
}
