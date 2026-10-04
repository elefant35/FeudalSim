using System.IO.Hashing;
using System.Runtime.InteropServices;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Social;

[Flags]
public enum ClaimQualifiers : byte { None = 0, Hedged = 1, Blurred = 2, Secret = 4 }

/// <summary>
/// A structured, interned statement (16 §7.1): <c>Predicate(Subject, Object, Magnitude, Time, Qualifiers)</c>.
/// <see cref="True"/> is ground truth (the sim always knows; people never do): set when the claim matches an observed event.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Claim
{
    public ulong Subject, Object;
    public long TimeMin;          // −1 when blurred
    public float Magnitude;
    public int DerivedFrom;       // −1 for an original
    public ushort Predicate;      // content handle
    public ClaimQualifiers Qualifiers;
    public byte True;
}

/// <summary>Interned claims in insertion order (ids are stable and deterministic); truth from observed events.</summary>
public sealed class ClaimStore
{
    private readonly List<Claim> _claims = [];
    private readonly Dictionary<(ushort, ulong, ulong, int, byte, long), int> _index = [];
    private readonly Dictionary<(ushort, ulong, ulong), List<int>> _observed = [];

    public int Count => _claims.Count;

    public ref readonly Claim this[int id] => ref CollectionsMarshal.AsSpan(_claims)[id];

    private static (ushort, ulong, ulong, int, byte, long) Key(in Claim c)
        => (c.Predicate, c.Subject, c.Object, (int)MathF.Round(c.Magnitude * 100f), (byte)c.Qualifiers, c.TimeMin);

    /// <summary>A claim that matches an event the sim just resolved: true by construction.</summary>
    public int Observe(int predicate, EntityId subject, EntityId obj, float magnitude, long nowMin)
    {
        var id = Intern(new Claim { Predicate = (ushort)predicate, Subject = subject.Value, Object = obj.Value, Magnitude = magnitude, TimeMin = nowMin, DerivedFrom = -1, True = 1 });
        var key = ((ushort)predicate, subject.Value, obj.Value);
        if (!_observed.TryGetValue(key, out var list)) { _observed[key] = list = []; }
        if (!list.Contains(id)) { list.Add(id); }
        return id;
    }

    /// <summary>Interns a (possibly mutated) claim; its truth is whether an observed event matches it (magnitude within 10 %, same time unless blurred).</summary>
    public int Intern(in Claim c)
    {
        var key = Key(c);
        if (_index.TryGetValue(key, out var id)) { return id; }
        var claim = c;
        if (claim.True == 0) { claim.True = Matches(claim) ? (byte)1 : (byte)0; }
        id = _claims.Count;
        _claims.Add(claim);
        _index[key] = id;
        return id;
    }

    private bool Matches(in Claim c)
    {
        if (!_observed.TryGetValue((c.Predicate, c.Subject, c.Object), out var list)) { return false; }
        foreach (var id in list)
        {
            ref readonly var o = ref this[id];
            if (MathF.Abs(o.Magnitude - c.Magnitude) <= 0.1f * MathF.Max(1f, o.Magnitude) && (c.TimeMin < 0 || c.TimeMin == o.TimeMin)) { return true; }
        }

        return false;
    }

    /// <summary>Walks <see cref="Claim.DerivedFrom"/> to the original claim.</summary>
    public int Root(int id)
    {
        while (this[id].DerivedFrom >= 0) { id = this[id].DerivedFrom; }
        return id;
    }

    internal void HashInto(XxHash64 h)
    {
        foreach (var c in _claims) { h.Append(MemoryMarshal.AsBytes(new ReadOnlySpan<Claim>(in c))); }
    }

    internal Claim[] Export() => [.. _claims];

    internal void Import(Claim[] rows)
    {
        Clear();
        foreach (var c in rows)
        {
            var id = _claims.Count;
            _claims.Add(c);
            _index[Key(c)] = id;
            if (c.True == 1 && c.DerivedFrom < 0)
            {
                var key = (c.Predicate, c.Subject, c.Object);
                if (!_observed.TryGetValue(key, out var list)) { _observed[key] = list = []; }
                list.Add(id);
            }
        }
    }

    private void Clear()
    {
        _claims.Clear();
        _index.Clear();
        _observed.Clear();
    }
}

/// <summary>One person's confidence in a claim (16 §7.1) with its source and telling state.</summary>
public sealed class Belief
{
    public int Claim;
    public float C;

    /// <summary>For first-hand beliefs: the confidence at witnessing (hearsay may lower it by ≤ 0.20).</summary>
    public float FirstHandC;

    public byte Hop;
    public bool FirstHand;
    public ulong Source;
    public long HeardMin, NovSinceMin, EagerUntilMin = -1, QuietUntilMin = -1;
    public readonly List<ulong> ToldTo = [];
}

/// <summary>Beliefs per person (16 §7). Forget below c 0.10; at most <see cref="Cap"/> per person (weakest evicted).</summary>
public sealed class BeliefStore
{
    public const float ForgetBelow = 0.10f;
    public const float Hold = 0.50f;

    /// <summary>16 sets no cap; 256 bounds memory and the per-exchange topic scan (beliefs about a 24-person camp stay far below it).</summary>
    public const int Cap = 256;

    private readonly SortedDictionary<ulong, List<Belief>> _byPerson = [];

    public int Count => _byPerson.Values.Sum(l => l.Count);

    public IReadOnlyList<Belief> Of(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? l : [];

    /// <summary>Allocation-free view for sim hot paths (valid until the next GetOrCreate/Forget).</summary>
    public ReadOnlySpan<Belief> Span(EntityId person) => _byPerson.TryGetValue(person.Value, out var l) ? CollectionsMarshal.AsSpan(l) : [];

    public Belief? Get(EntityId person, int claim)
    {
        if (!_byPerson.TryGetValue(person.Value, out var list)) { return null; }
        foreach (var b in list) { if (b.Claim == claim) { return b; } }
        return null;
    }

    public Belief GetOrCreate(EntityId person, int claim, long nowMin)
    {
        if (Get(person, claim) is { } b) { return b; }
        if (!_byPerson.TryGetValue(person.Value, out var list)) { _byPerson[person.Value] = list = []; }
        b = new Belief { Claim = claim, HeardMin = nowMin, NovSinceMin = nowMin };
        list.Add(b);
        if (list.Count > Cap)
        {
            var weakest = 0;
            for (var k = 1; k < list.Count - 1; k++) { if (list[k].C < list[weakest].C) { weakest = k; } }
            list.RemoveAt(weakest);
        }

        return b;
    }

    /// <summary>Drops beliefs below the forget threshold (nightly).</summary>
    public void Forget()
    {
        foreach (var list in _byPerson.Values) { list.RemoveAll(b => b.C < ForgetBelow); }
    }

    /// <summary>How many people other than the claim's subject hold a belief derived from <paramref name="root"/> at c ≥ <paramref name="min"/> (any variant).</summary>
    public int Holders(ClaimStore claims, int root, float min, out int variantHolders)
    {
        int n = 0, variants = 0;
        var subject = claims[root].Subject;
        foreach (var (owner, list) in _byPerson)
        {
            if (owner == subject) { continue; }
            bool any = false, variant = false;
            foreach (var b in list)
            {
                if (b.C < min || claims.Root(b.Claim) != root) { continue; }
                any = true;
                variant |= b.Claim != root;
            }

            if (any) { n++; }
            if (variant) { variants++; }
        }

        variantHolders = variants;
        return n;
    }

    internal void HashInto(XxHash64 h)
    {
        var buf = new byte[8];
        void L(long v) { BitConverter.TryWriteBytes(buf, v); h.Append(buf); }
        foreach (var (person, list) in _byPerson)
        {
            L((long)person);
            foreach (var b in list)
            {
                L(b.Claim); L(BitConverter.SingleToInt32Bits(b.C)); L(BitConverter.SingleToInt32Bits(b.FirstHandC)); L(b.Hop); L(b.FirstHand ? 1 : 0);
                L((long)b.Source); L(b.HeardMin); L(b.NovSinceMin); L(b.EagerUntilMin); L(b.QuietUntilMin);
                foreach (var t in b.ToldTo) { L((long)t); }
                L(-1);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Row
    {
        public ulong Owner, Source;
        public long HeardMin, NovSinceMin, EagerUntilMin, QuietUntilMin;
        public int Claim, ToldStart, ToldCount;
        public float C, FirstHandC;
        public byte Hop, FirstHand;
    }

    internal (Row[] Rows, ulong[] Told) Export()
    {
        var rows = new List<Row>();
        var told = new List<ulong>();
        foreach (var (person, list) in _byPerson)
        {
            foreach (var b in list)
            {
                rows.Add(new Row
                {
                    Owner = person, Source = b.Source, HeardMin = b.HeardMin, NovSinceMin = b.NovSinceMin, EagerUntilMin = b.EagerUntilMin, QuietUntilMin = b.QuietUntilMin,
                    Claim = b.Claim, ToldStart = told.Count, ToldCount = b.ToldTo.Count, C = b.C, FirstHandC = b.FirstHandC, Hop = b.Hop, FirstHand = b.FirstHand ? (byte)1 : (byte)0,
                });
                told.AddRange(b.ToldTo);
            }
        }

        return ([.. rows], [.. told]);
    }

    internal void Import(Row[] rows, ulong[] told)
    {
        _byPerson.Clear();
        foreach (var r in rows)
        {
            if (!_byPerson.TryGetValue(r.Owner, out var list)) { _byPerson[r.Owner] = list = []; }
            var b = new Belief
            {
                Claim = r.Claim, C = r.C, FirstHandC = r.FirstHandC, Hop = r.Hop, FirstHand = r.FirstHand != 0, Source = r.Source,
                HeardMin = r.HeardMin, NovSinceMin = r.NovSinceMin, EagerUntilMin = r.EagerUntilMin, QuietUntilMin = r.QuietUntilMin,
            };
            b.ToldTo.AddRange(told.AsSpan(r.ToldStart, r.ToldCount).ToArray());
            list.Add(b);
        }
    }
}
