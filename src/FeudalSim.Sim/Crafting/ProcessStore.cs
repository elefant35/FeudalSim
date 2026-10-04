using System.IO.Hashing;
using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Crafting;

/// <summary>13 §4.2 process states (byte values saved; append only).</summary>
public enum ProcessState : byte { Active, Held, Waiting, Ready, Overrun, Complete, Ruined }

/// <summary>
/// 13 §4.1 ProcessInstance: a WIP tied to its recipe and stage. Stage results commit at stage end (PS per stage, flaws);
/// <c>BusyUntilMin</c> is when the current labor (or passive timer) ends; <c>LaborOwedMin</c> is labor still owed after an
/// interruption (Held). Saved and hashed.
/// </summary>
[MessagePackObject]
public sealed class Process
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public int Recipe { get; set; }
    [Key(2)] public EntityId Worker { get; set; }
    [Key(3)] public EntityId Container { get; set; }
    [Key(4)] public int Stage { get; set; }
    [Key(5)] public ProcessState State { get; set; }
    [Key(6)] public float[] StagePs { get; set; } = [];
    [Key(7)] public ulong Flaws { get; set; }
    [Key(8)] public float Material { get; set; }
    [Key(9)] public bool Masterwork { get; set; }
    [Key(10)] public long StartedMin { get; set; }
    [Key(11)] public long BusyUntilMin { get; set; }
    [Key(12)] public float LaborMin { get; set; }
    [Key(13)] public float LaborOwedMin { get; set; }
    [Key(14)] public int Cap { get; set; } = 100;

    /// <summary>Passive stage: ready at BusyUntilMin, ideal window ends at IdealUntilMin, then overrun penalties.</summary>
    [Key(15)] public long IdealUntilMin { get; set; }

    [Key(16)] public float OverrunQ { get; set; }

    /// <summary>The world node worked (M2-12): chunk, index in its generated list, and its size class; −1 when none.</summary>
    [Key(17)] public int SiteChunk { get; set; } = -1;

    [Key(18)] public int SiteIndex { get; set; } = -1;

    [Key(19)] public byte SiteSize { get; set; }
}

/// <summary>Open processes by id plus completions per (person, recipe) for Quick Work and Batch (13 §12). Saved and hashed.</summary>
public sealed class ProcessStore
{
    private readonly SortedDictionary<ulong, Process> _open = [];
    private readonly SortedDictionary<(ulong Person, int Recipe), int> _completions = [];
    private ulong _lastId;

    public int Count => _open.Count;

    public IEnumerable<Process> Open => _open.Values;

    public Process? Get(ulong id) => _open.TryGetValue(id, out var p) ? p : null;

    public int Completions(EntityId person, int recipe) => _completions.GetValueOrDefault((person.Value, recipe));

    internal ulong NextId() => ++_lastId;

    internal void Add(Process p) => _open.Add(p.Id, p);

    internal void Remove(ulong id) => _open.Remove(id);

    internal void Completed(EntityId person, int recipe) => _completions[(person.Value, recipe)] = Completions(person, recipe) + 1;

    [MessagePackObject]
    public sealed class Snapshot
    {
        [Key(0)] public Process[] Open { get; set; } = [];
        [Key(1)] public ulong[] CompletionKeys { get; set; } = [];
        [Key(2)] public int[] CompletionRecipes { get; set; } = [];
        [Key(3)] public int[] CompletionCounts { get; set; } = [];
        [Key(4)] public ulong LastId { get; set; }
    }

    internal Snapshot Export() => new()
    {
        Open = [.. _open.Values], CompletionKeys = [.. _completions.Keys.Select(k => k.Person)],
        CompletionRecipes = [.. _completions.Keys.Select(k => k.Recipe)], CompletionCounts = [.. _completions.Values], LastId = _lastId,
    };

    internal void Import(Snapshot s)
    {
        _open.Clear();
        _completions.Clear();
        foreach (var p in s.Open) { _open.Add(p.Id, p); }
        for (var k = 0; k < s.CompletionKeys.Length; k++) { _completions[(s.CompletionKeys[k], s.CompletionRecipes[k])] = s.CompletionCounts[k]; }
        _lastId = s.LastId;
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        void Put(long v, Span<byte> buf) { BitConverter.TryWriteBytes(buf, v); h.Append(buf); }
        Put((long)_lastId, b);
        foreach (var p in _open.Values)
        {
            Put((long)p.Id, b); Put(p.Recipe, b); Put((long)p.Worker.Value, b); Put((long)p.Container.Value, b); Put(p.Stage, b); Put((long)p.State, b);
            foreach (var ps in p.StagePs) { Put(BitConverter.SingleToInt32Bits(ps), b); }
            Put((long)p.Flaws, b); Put(BitConverter.SingleToInt32Bits(p.Material), b); Put(p.Masterwork ? 1 : 0, b); Put(p.StartedMin, b); Put(p.BusyUntilMin, b);
            Put(BitConverter.SingleToInt32Bits(p.LaborMin), b); Put(BitConverter.SingleToInt32Bits(p.LaborOwedMin), b); Put(p.Cap, b); Put(p.IdealUntilMin, b);
            Put(BitConverter.SingleToInt32Bits(p.OverrunQ), b); Put(p.SiteChunk, b); Put(p.SiteIndex, b); Put(p.SiteSize, b);
        }

        foreach (var ((person, recipe), n) in _completions) { Put((long)person, b); Put(recipe, b); Put(n, b); }
    }
}
