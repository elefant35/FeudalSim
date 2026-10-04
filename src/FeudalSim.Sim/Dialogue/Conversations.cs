using System.IO.Hashing;
using System.Text;
using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Dialogue;

/// <summary>
/// One conversation between the player's character and an NPC (21 §14.4, 22 §4.11). Turns advance only through logged
/// <see cref="Commands.PlayerUtteranceClassified"/> commands; each NPC turn opens the NPC's DPs (the initiative DP here;
/// response DPs with their owners, M1-08–10). Saved and hashed.
/// </summary>
[MessagePackObject]
public sealed class Conversation
{
    [Key(0)] public ulong Id { get; set; }
    [Key(1)] public EntityId Npc { get; set; }
    [Key(2)] public EntityId Player { get; set; }
    [Key(3)] public long OpenedStep { get; set; }

    /// <summary>The player's last turn index (0 = none yet).</summary>
    [Key(4)] public int Turn { get; set; }

    /// <summary>The last classified player act (catalog v2 id, 22 §5.2).</summary>
    [Key(5)] public string LastAct { get; set; } = "";

    /// <summary>Initiative pacing (21 §14.5): option → the NPC turn it was last acted on.</summary>
    [Key(6)] public SortedDictionary<string, int> LastActed { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Initiative pacing: option → times the player declined it in this conversation.</summary>
    [Key(7)] public SortedDictionary<string, int> Declined { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The initiative the NPC offered on the last turn (the player answers it in their next turn), or "".</summary>
    [Key(8)] public string PendingOffer { get; set; } = "";

    /// <summary>The approach intent (21 §14.1) when the NPC opened the conversation, else "" (player-initiated).</summary>
    [Key(9)] public string Agenda { get; set; } = "";

    /// <summary>DPs opened for this conversation that may still be open (cancelled when it ends, 21 §14.6).</summary>
    [Key(10)] public List<ulong> Dps { get; set; } = [];

    /// <summary>What the NPC was doing when the conversation began (content action handle, −1 none): a task to ask help with.</summary>
    [Key(11)] public short PrevAction { get; set; } = -1;
}

/// <summary>Open conversations in id order. State: saved (the <c>conversations</c> table) and hashed.</summary>
public sealed class ConversationStore
{
    private readonly SortedDictionary<ulong, Conversation> _open = [];
    private ulong _lastId;

    public int Count => _open.Count;

    public IEnumerable<Conversation> Open => _open.Values;

    public Conversation? Get(ulong id) => _open.TryGetValue(id, out var c) ? c : null;

    /// <summary>The open conversation an NPC is in, or null (one at a time).</summary>
    public Conversation? Of(EntityId npc)
    {
        foreach (var c in _open.Values) { if (c.Npc == npc) { return c; } }
        return null;
    }

    internal Conversation Add(EntityId npc, EntityId player, long step, string agenda, short prevAction)
    {
        var c = new Conversation { Id = ++_lastId, Npc = npc, Player = player, OpenedStep = step, Agenda = agenda, PrevAction = prevAction };
        _open.Add(c.Id, c);
        return c;
    }

    internal bool Remove(ulong id) => _open.Remove(id);

    internal (Conversation[] Open, ulong LastId) Export() => ([.. _open.Values], _lastId);

    internal void Import(Conversation[] open, ulong lastId)
    {
        _open.Clear();
        foreach (var c in open)
        {
            // The deserializer builds dictionaries with the default (culture) comparer; pacing keys iterate in ordinal order.
            c.LastActed = new SortedDictionary<string, int>(c.LastActed, StringComparer.Ordinal);
            c.Declined = new SortedDictionary<string, int>(c.Declined, StringComparer.Ordinal);
            _open.Add(c.Id, c);
        }
        _lastId = lastId;
    }

    internal void HashInto(XxHash64 h)
    {
        L(h, (long)_lastId);
        foreach (var c in _open.Values)
        {
            L(h, (long)c.Id); L(h, (long)c.Npc.Value); L(h, (long)c.Player.Value); L(h, c.OpenedStep); L(h, c.Turn); L(h, c.PrevAction);
            S(h, c.LastAct); S(h, c.PendingOffer); S(h, c.Agenda);
            foreach (var (k, v) in c.LastActed) { S(h, k); L(h, v); }
            foreach (var (k, v) in c.Declined) { S(h, k); L(h, v); }
            foreach (var dp in c.Dps) { L(h, (long)dp); }
        }
    }

    private static void L(XxHash64 h, long v)
    {
        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, v);
        h.Append(b);
    }

    private static void S(XxHash64 h, string s)
    {
        L(h, s.Length);
        h.Append(Encoding.UTF8.GetBytes(s));
    }
}
