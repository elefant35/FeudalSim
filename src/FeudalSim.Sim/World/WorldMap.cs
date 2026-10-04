using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.WorldGen;

namespace FeudalSim.Sim.World;

/// <summary>
/// The generated region a game is played in (M2-02): the grids, the seeds, the landing, POIs and deposits. Immutable once
/// attached — what changes later (felled trees, picked herbs) is a <see cref="NodeDeltaStore"/>. 10 §3.2: the grids are
/// written into the save, so float drift across platforms can never change a world after creation. The state hash takes
/// the map's <see cref="Fingerprint"/> (computed once) rather than re-hashing 11 MB every step.
/// </summary>
public sealed class WorldMap
{
    public const int CodecVersion = 1;

    public WorldMap(WorldGrid grid, ulong seed, ulong attemptSeed, string specId, LandingSite? landing, IReadOnlyList<Poi> pois, IReadOnlyList<Deposit> deposits)
    {
        (Grid, Seed, AttemptSeed, SpecId, Landing, Pois, Deposits) = (grid, seed, attemptSeed, specId, landing, pois, deposits);
        Fingerprint = ComputeFingerprint();
    }

    public WorldGrid Grid { get; }
    public ulong Seed { get; }
    public ulong AttemptSeed { get; }
    public string SpecId { get; }
    public LandingSite? Landing { get; }
    public IReadOnlyList<Poi> Pois { get; }
    public IReadOnlyList<Deposit> Deposits { get; }
    public ulong Fingerprint { get; }

    public static WorldMap From(WorldGenResult w) => new(w.Grid, w.Seed, w.AttemptSeed, w.SpecId, w.Landing, w.Pois, w.Deposits);

    private ulong ComputeFingerprint()
    {
        var h = new XxHash64();
        h.Append(MemoryMarshal.AsBytes(Grid.Height.AsSpan()));
        foreach (var b in Grids(Grid)) { h.Append(b); }
        Span<byte> buf = stackalloc byte[8];
        BitConverter.TryWriteBytes(buf, AttemptSeed);
        h.Append(buf);
        return h.GetCurrentHashAsUInt64();
    }

    /// <summary>The byte grids in a fixed order (the codec and the fingerprint share it).</summary>
    internal static byte[][] Grids(WorldGrid g) => [g.Land, g.Lithology, g.Water, g.Slope, g.Biome, g.Soil, g.Fertility, g.Exposure, g.RainShadow, g.ClimateFlags];

    /// <summary>
    /// Pure byte codec (no streams): header, the float heights and coast distances, the byte grids, then the landing, POIs
    /// and deposits. Little-endian, versioned.
    /// </summary>
    public byte[] Encode()
    {
        var g = Grid;
        var w = new Bytes(16 + (g.Size * g.Size * 18) + 4096);
        w.Raw("FSWM"u8);
        w.I32(CodecVersion);
        w.I32(g.Size);
        w.F32(g.CellM);
        w.U64(Seed);
        w.U64(AttemptSeed);
        w.Str(SpecId);
        w.Raw(MemoryMarshal.AsBytes(g.Height.AsSpan()));
        w.Raw(MemoryMarshal.AsBytes(g.CoastDistM.AsSpan()));
        foreach (var b in Grids(g)) { w.Raw(b); }
        w.U8(Landing is null ? (byte)0 : (byte)1);
        if (Landing is { } l) { foreach (var f in (ReadOnlySpan<float>)[l.BeachX, l.BeachZ, l.WreckX, l.WreckZ, l.ReefM, l.WaterM, l.FlintM, l.ClayM, l.BroadleafM, l.FertileHa, l.Score]) { w.F32(f); } }
        w.I32(Pois.Count);
        foreach (var p in Pois)
        {
            w.I32(p.Id);
            w.U8((byte)p.Kind);
            w.F32(p.X);
            w.F32(p.Z);
        }

        w.I32(Deposits.Count);
        foreach (var d in Deposits)
        {
            w.I32(d.Id);
            w.U8((byte)d.Kind);
            w.F32(d.X);
            w.F32(d.Z);
            w.F32(d.RadiusM);
            w.F32(d.Grade);
            w.I32(d.DetectDifficulty);
        }

        return w.ToArray();
    }

    public static WorldMap Decode(byte[] data)
    {
        var r = new Reader(data);
        if (!r.Raw(4).SequenceEqual("FSWM"u8)) { throw new InvalidDataException("Not a FeudalSim world map."); }
        if (r.I32() != CodecVersion) { throw new InvalidDataException("Unsupported world map version."); }
        var size = r.I32();
        var cell = r.F32();
        var (seed, attempt, spec) = (r.U64(), r.U64(), r.Str());
        var g = new WorldGrid(size, cell);
        r.Raw(size * size * 4).CopyTo(MemoryMarshal.AsBytes(g.Height.AsSpan()));
        r.Raw(size * size * 4).CopyTo(MemoryMarshal.AsBytes(g.CoastDistM.AsSpan()));
        foreach (var b in Grids(g)) { r.Raw(b.Length).CopyTo(b); }
        LandingSite? landing = null;
        if (r.U8() == 1)
        {
            Span<float> f = stackalloc float[11];
            for (var k = 0; k < f.Length; k++) { f[k] = r.F32(); }
            landing = new LandingSite(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10]);
        }

        var pois = new List<Poi>();
        for (var k = r.I32(); k > 0; k--) { pois.Add(new Poi(r.I32(), (PoiKind)r.U8(), r.F32(), r.F32())); }
        var deposits = new List<Deposit>();
        for (var k = r.I32(); k > 0; k--) { deposits.Add(new Deposit(r.I32(), (DepositKind)r.U8(), r.F32(), r.F32(), r.F32(), r.F32(), r.I32())); }
        return new WorldMap(g, seed, attempt, spec, landing, pois, deposits);
    }

    /// <summary>A growable little-endian byte writer (no streams).</summary>
    private sealed class Bytes(int capacity)
    {
        private byte[] _buf = new byte[capacity];
        private int _len;

        private Span<byte> Take(int n)
        {
            if (_len + n > _buf.Length) { Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + n)); }
            var s = _buf.AsSpan(_len, n);
            _len += n;
            return s;
        }

        public void Raw(ReadOnlySpan<byte> b) => b.CopyTo(Take(b.Length));
        public void U8(byte v) => Take(1)[0] = v;
        public void I32(int v) => BinaryPrimitives.WriteInt32LittleEndian(Take(4), v);
        public void U64(ulong v) => BinaryPrimitives.WriteUInt64LittleEndian(Take(8), v);
        public void F32(float v) => BinaryPrimitives.WriteSingleLittleEndian(Take(4), v);

        public void Str(string s)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            I32(bytes.Length);
            Raw(bytes);
        }

        public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();
    }

    private sealed class Reader(byte[] data)
    {
        private int _at;

        public ReadOnlySpan<byte> Raw(int n)
        {
            if (_at + n > data.Length) { throw new InvalidDataException("Truncated world map."); }
            var s = data.AsSpan(_at, n);
            _at += n;
            return s;
        }

        public byte U8() => Raw(1)[0];
        public int I32() => BinaryPrimitives.ReadInt32LittleEndian(Raw(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64LittleEndian(Raw(8));
        public float F32() => BinaryPrimitives.ReadSingleLittleEndian(Raw(4));
        public string Str() => System.Text.Encoding.UTF8.GetString(Raw(I32()));
    }
}

/// <summary>
/// 20 §6.6 node deltas (M2-02): only what changed from the generated nodes is kept — per (chunk, index in the chunk's
/// generated list), the node's new state (felled, stump, harvested …; 0 removes the delta). Saved and hashed.
/// </summary>
public sealed class NodeDeltaStore
{
    private readonly SortedDictionary<(int Chunk, int Index), byte> _deltas = [];

    public int Count => _deltas.Count;

    public byte State(int chunk, int index) => _deltas.GetValueOrDefault((chunk, index));

    public void Set(int chunk, int index, byte state)
    {
        if (state == 0) { _deltas.Remove((chunk, index)); } else { _deltas[(chunk, index)] = state; }
    }

    /// <summary>Clears every delta in a state (spring regrowth of picked plants, 10 §7.4).</summary>
    public void ClearState(byte state)
    {
        foreach (var key in _deltas.Where(kv => kv.Value == state).Select(kv => kv.Key).ToList()) { _deltas.Remove(key); }
    }

    /// <summary>Applies the deltas to a freshly generated chunk.</summary>
    public void Apply(int chunk, List<ResourceNode> nodes)
    {
        for (var k = 0; k < nodes.Count; k++)
        {
            if (_deltas.TryGetValue((chunk, k), out var state)) { var n = nodes[k]; n.State = state; nodes[k] = n; }
        }
    }

    internal (int[] Chunks, int[] Indices, byte[] States) Export() => ([.. _deltas.Keys.Select(k => k.Chunk)], [.. _deltas.Keys.Select(k => k.Index)], [.. _deltas.Values]);

    internal void Import(int[] chunks, int[] indices, byte[] states)
    {
        _deltas.Clear();
        for (var k = 0; k < chunks.Length; k++) { _deltas[(chunks[k], indices[k])] = states[k]; }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var ((chunk, index), state) in _deltas)
        {
            BitConverter.TryWriteBytes(b, ((long)chunk << 32) | (uint)index);
            h.Append(b);
            h.Append([state]);
        }
    }
}
