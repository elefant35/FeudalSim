using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Content;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim.Dialogue;

/// <summary>
/// Conversations with the player (21 §14.4–14.6, 22 §4.11), sim side. Commands open and end them and advance turns;
/// each NPC turn opens the NPC's decision points (the 21 §14.5 initiative DP here; response DPs join with their owning
/// systems in M1-08–10). Each step this system keeps the NPC held in <c>action.converse</c>, marks it Deliberating while
/// its DPs are open (§14.6: listening idles, never a hint of the pending choice), and ends conversations on a P0
/// interrupt (a threat: open DPs are cancelled), when the player walks off (&gt; 8 m) or when the NPC falls asleep.
/// </summary>
public sealed class ConversationSystem : ISimSystem
{
    /// <summary>The player starts a conversation within this distance (22 §4.11 prefetch range).</summary>
    public const float StartRangeM = 6f;

    /// <summary>It ends when they are farther apart than this.</summary>
    public const float LeaveRangeM = 8f;

    /// <summary>Injection probability at or above which the turn's DPs are decided by the policy (22 §4.12).</summary>
    public const float InjectionPolicy = 0.3f;

    public string Name => "Conversations";
    public SimPhase Phase => SimPhase.Sense;   // before the utility AI decides

    public void Run(in StepContext ctx, SimWorld world)
    {
        var store = world.Conversations;
        if (store.Count == 0) { return; }
        List<(Conversation C, string Reason)>? ending = null;
        static (Conversation, string) E(Conversation c, string r) => (c, r);
        foreach (var c in store.Open)
        {
            var npc = world.People.IndexOf(c.Npc);
            var player = world.People.IndexOf(c.Player);
            if (npc < 0 || player < 0) { (ending ??= []).Add(E(c, "gone")); continue; }
            if (world.Camp.Threat >= 1f) { (ending ??= []).Add(E(c, "p0")); continue; }   // 21 §14.6: P0 ends it at once, not a decision
            ref readonly var a = ref world.People.Transforms[npc];
            ref readonly var b = ref world.People.Transforms[player];
            float dx = a.X - b.X, dz = a.Z - b.Z;
            if ((dx * dx) + (dz * dz) > LeaveRangeM * LeaveRangeM) { (ending ??= []).Add(E(c, "left")); continue; }

            c.Dps.RemoveAll(id => !world.Decisions.IsOpen(id));
            ref var act = ref world.People.Activity[npc];
            act.Flags = (byte)((act.Flags & ~ActivityState.Deliberating) | (c.Dps.Count > 0 ? ActivityState.Deliberating : 0));
        }

        if (ending is null) { return; }
        foreach (var (c, reason) in ending) { End(world, c, reason); }
    }

    internal static void Start(SimWorld world, in CommandEnvelope command, StartConversation c)
    {
        var npc = world.People.IndexOf(c.Npc);
        var player = world.PlayerRow;
        string? problem = null;
        if (player < 0) { problem = "no player character"; }
        else if (npc < 0 || npc == player) { problem = $"no such person {c.Npc}"; }
        else if (world.Conversations.Of(c.Npc) is not null || world.Conversations.Count > 0) { problem = "already in a conversation"; }
        else if (world.People.Activity[npc].Has(ActivityState.Asleep)) { problem = "asleep"; }
        else
        {
            ref readonly var a = ref world.People.Transforms[npc];
            ref readonly var b = ref world.People.Transforms[player];
            float dx = a.X - b.X, dz = a.Z - b.Z;
            if ((dx * dx) + (dz * dz) > StartRangeM * StartRangeM) { problem = "too far away"; }
        }

        var converse = ContentDatabase.HandleOf(world.Content.Actions, "action.converse", d => d.Id);
        if (problem is null && converse < 0) { problem = "content has no action.converse"; }
        if (problem is not null)
        {
            world.RejectCommand(command, $"StartConversation: {problem}.");
            return;
        }

        ref var act = ref world.People.Activity[npc];
        var prev = act.Action >= 0 && act.Has(ActivityState.Purposeful) ? act.Action : (short)-1;
        var conv = world.Conversations.Add(c.Npc, world.PlayerId, world.Clock.Step, "", prev);
        var now = world.Clock.GameMs;
        act = new ActivityState
        {
            Action = (short)converse, Phase = 1, Level = ActivityLevel.Rest,
            Flags = ActivityState.Interacting | ActivityState.Conversing,
            StartedGameMs = now, EndGameMs = long.MaxValue, NextDecideGameMs = long.MaxValue,
            TargetX = world.People.Transforms[npc].X, TargetZ = world.People.Transforms[npc].Z,
        };
        world.People.Wander[npc].HasTarget = false;   // a body stops walking and faces the player
        world.Emit(Salience.Minor, c.Npc, new ConversationStarted(conv.Id, c.Npc, world.PlayerId));
    }

    internal static void EndByPlayer(SimWorld world, in CommandEnvelope command, EndConversation c)
    {
        if (world.Conversations.Get(c.Conversation) is not { } conv) { world.RejectCommand(command, $"EndConversation: no open conversation {c.Conversation}."); return; }
        End(world, conv, "player");
    }

    /// <summary>Closes a conversation: cancels its open DPs (a later decision is rejected) and frees the NPC to decide next step.</summary>
    public static void End(SimWorld world, Conversation conv, string reason)
    {
        if (!world.Conversations.Remove(conv.Id)) { return; }
        foreach (var dp in conv.Dps) { world.Decisions.Cancel(dp, $"conversation ended ({reason})"); }
        var npc = world.People.IndexOf(conv.Npc);
        if (npc >= 0)
        {
            ref var act = ref world.People.Activity[npc];
            act.Flags = (byte)(act.Flags & ~(ActivityState.Conversing | ActivityState.Deliberating | ActivityState.Interacting));
            act.EndGameMs = world.Clock.GameMs;   // the utility AI picks the next activity
            act.NextDecideGameMs = world.Clock.GameMs;
        }

        world.Emit(Salience.Minor, conv.Npc, new ConversationEnded(conv.Id, conv.Npc, reason));
    }

    /// <summary>A player turn (22 §4.7): pacing bookkeeping, then the NPC's DPs for this turn.</summary>
    internal static void Utterance(SimWorld world, in CommandEnvelope command, PlayerUtteranceClassified u)
    {
        if (world.Conversations.Get(u.Conversation) is not { } conv) { world.RejectCommand(command, $"Utterance for no open conversation {u.Conversation}."); return; }
        if (u.TurnIndex != conv.Turn + 1) { world.RejectCommand(command, $"Utterance turn {u.TurnIndex}, expected {conv.Turn + 1}."); return; }
        if (!float.IsFinite(u.ActP) || !float.IsFinite(u.Injection) || u.Act.Length is 0 or > 40) { world.RejectCommand(command, "Malformed utterance."); return; }

        // An offer the NPC made last turn is answered by this act; a refusal counts toward the re-ask limit (21 §14.5).
        if (conv.PendingOffer.Length > 0 && u.Act is "reject_offer" or "refuse")
        {
            conv.Declined[conv.PendingOffer] = conv.Declined.GetValueOrDefault(conv.PendingOffer) + 1;
        }

        conv.PendingOffer = "";
        conv.Turn = u.TurnIndex;
        conv.LastAct = u.Act;

        // The response DP for the act (16 escalation, 16 social, 15 trade…) is opened here by its owner (M1-08–10).
        var decider = u.Injection >= InjectionPolicy ? DeciderKind.Policy : DeciderKind.Llm;
        var id = world.Decisions.Open(InitiativeOwner.Id, new DpContext(InitiativeOwner.Kind, conv.Npc, conv.Player, (long)conv.Id), decider,
            DecisionRulesEngine.ConversationDeadlineSteps);
        if (world.Decisions.IsOpen(id)) { conv.Dps.Add(id); }
    }
}
