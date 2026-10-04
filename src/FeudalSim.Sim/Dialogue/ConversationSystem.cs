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

    /// <summary>22 §4.10: the rendered NPC line joins the transcript (late lines for an ended conversation are dropped).</summary>
    internal static void Line(SimWorld world, in CommandEnvelope command, DialogueLineRendered c)
    {
        if (world.Conversations.Get(c.Conversation) is not { } conv) { return; }
        var speaker = world.People.IndexOf(c.Speaker);
        conv.AddLine($"{(speaker >= 0 ? world.People.Names[speaker] : "?")}: {c.Text}");
    }

    private static void Track(SimWorld world, Conversation conv, ulong dp)
    {
        if (world.Decisions.IsOpen(dp)) { conv.Dps.Add(dp); }
    }

    /// <summary>22 §6.3 appeal match: (value − 50)/50 for one of the nine values; pity +0.5 for the Compassionate or
    /// Charitable; flattery +0.5 for the Proud, −0.5 for the Humble; otherwise 0.</summary>
    public static float AppealMatch(SimWorld world, int listener, string appeal)
    {
        var p = world.People.Personality[listener];
        bool Has(string t) => p.HasTrait(world.Content.TraitHandle(t));
        var v = p.Values;
        return appeal switch
        {
            "family" => (v.Family - 50f) / 50f, "wealth" => (v.Wealth - 50f) / 50f, "status" => (v.Status - 50f) / 50f,
            "honor" => (v.Honor - 50f) / 50f, "tradition" => (v.Tradition - 50f) / 50f, "faith" => (v.Faith - 50f) / 50f,
            "fairness" => (v.Fairness - 50f) / 50f, "freedom" => (v.Freedom - 50f) / 50f, "loyalty" => (v.Loyalty - 50f) / 50f,
            "pity" => Has("trait.compassionate") || Has("trait.charitable") ? 0.5f : 0f,
            "flattery" => Has("trait.proud") ? 0.5f : Has("trait.humble") ? -0.5f : 0f,
            _ => 0f,
        };
    }

    /// <summary>The player's own act is applied before the NPC's answer, deterministically (22 §6.1): modifier, memory, claim.</summary>
    private static void CommitProvocation(SimWorld world, int player, int npc, string act, int severity)
    {
        var people = world.People;
        EntityId p = people.Ids[player], r = people.Ids[npc];
        var witnesses = Social.Escalation.Witnesses(world, player, npc, 10f);
        var now = world.Clock.GameMinute;
        if (act == "threaten")
        {
            world.Relationships.ApplyModifier(r, p, "opinion.threatened_me", isPublic: witnesses >= 3);
            Social.Rumors.Witness(world, "claim.threatened", player, npc, 1f, Social.EscalationOwner.Earshot);
        }
        else
        {
            world.Relationships.ApplyModifier(r, p, Social.Escalation.ModifierFor(severity), isPublic: witnesses >= 3);
            Social.Rumors.Witness(world, "claim.insulted", player, npc, 1f, Social.EscalationOwner.Earshot);
        }

        world.Memories.Remember(r, Social.MemoryKind.Insult, p, r, now, 30, 1f, 25f, -60);
    }

    /// <summary>Closes a conversation: cancels its open DPs (a later decision is rejected) and frees the NPC to decide next step.</summary>
    public static void End(SimWorld world, Conversation conv, string reason)
    {
        if (!world.Conversations.Remove(conv.Id)) { return; }
        foreach (var dp in conv.Dps) { world.Decisions.Cancel(dp, $"conversation ended ({reason})"); }

        // 16 §4.15: the close brings a rapport DP — in the closing reply when there is one, else the policy decides.
        if (conv.Turn > 0 && conv.RapportCount < Social.RapportOwner.MaxPerConversation && world.People.IndexOf(conv.Player) >= 0 && world.People.IndexOf(conv.Npc) >= 0)
        {
            conv.RapportCount++;
            var decider = reason is "player" or "npc" && !conv.PolicyTurn ? DeciderKind.Llm : DeciderKind.Policy;
            world.Decisions.Open(Social.RapportOwner.Id, new DpContext(Social.RapportOwner.Kind, conv.Npc, conv.Player, Social.RapportOwner.Encode(conv.MeanWords)),
                decider, DecisionRulesEngine.ConversationDeadlineSteps);
        }

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
        conv.AddLine($"{(world.People.IndexOf(conv.Player) is var pr and >= 0 ? world.People.Names[pr] : "Player")}: {u.Text}");
        conv.Turn = u.TurnIndex;
        conv.LastAct = u.Act;
        var listener = world.People.IndexOf(conv.Npc);
        var lWords = listener < 0 ? 0f : MenuWidth.LWords(u.Persuasiveness, AppealMatch(world, listener, u.Appeal), u.Hostility, u.Politeness);
        conv.WordsSum += lWords;
        conv.WordsN++;

        // The response DP for the act (22 §6.1 routing). M1-08: insult and threaten → 16's escalation ladder; the social
        // (M1-09) and trade (M1-10) owners join here.
        var decider = u.Injection >= InjectionPolicy ? DeciderKind.Policy : DeciderKind.Llm;
        conv.PolicyTurn = decider == DeciderKind.Policy;
        var npc = world.People.IndexOf(conv.Npc);
        var player = world.People.IndexOf(conv.Player);
        if (npc >= 0 && player >= 0 && Social.Escalation.DefaultSeverity(u.Act) is var s and > 0)
        {
            var severity = u.Severity > 0 ? Math.Clamp(u.Severity, 1, 5) : s;
            CommitProvocation(world, player, npc, u.Act, severity);
            var response = Social.Escalation.Provoke(world, player, npc, severity, decider, DecisionRulesEngine.ConversationDeadlineSteps);
            if (world.Decisions.IsOpen(response)) { conv.Dps.Add(response); }
            if (world.Conversations.Get(conv.Id) is null) { return; }   // the answer ended it (walked off, a fight)
        }

        if (npc >= 0 && player >= 0 && u.Act == "apologize")
        {
            // 16 §4.14: the attempt is remembered (a fourth within 4 days reads as mockery), then the victim answers.
            var me = world.People.Ids[npc];
            if (Social.ApologyOwner.RecentApologies(world, me, conv.Player) >= 3) { world.Relationships.ApplyModifier(me, conv.Player, "opinion.rude_to_me"); }
            world.Memories.Remember(me, Social.MemoryKind.Apology, conv.Player, me, world.Clock.GameMinute, 15, 1f, 0f, 0);
            var sincerity = u.Sincerity > 0f ? Math.Clamp((u.Sincerity - 3f) / 2f, -1f, 1f) : 0f;
            Track(world, conv, world.Decisions.Open(Social.ApologyOwner.Id, new DpContext(Social.ApologyOwner.Kind, conv.Npc, conv.Player, Social.RapportOwner.Encode(sincerity)),
                decider, DecisionRulesEngine.ConversationDeadlineSteps));
        }

        if (npc >= 0 && player >= 0 && u.Act == "request")
        {
            // 16 §5.4: n-th ask today → acceptance × 0.5^(n−1), Anger +3 from the second, rude_to_me from the third.
            var me = world.People.Ids[npc];
            var n = world.Relationships.CountAsk(me, conv.Player);
            if (n >= 2)
            {
                ref var e = ref world.People.Emotions[npc];
                (e.Anger, e.AngerTarget) = (MathF.Min(100f, e.Anger + 3f), conv.Player);
            }

            if (n >= 3) { world.Relationships.ApplyModifier(me, conv.Player, "opinion.rude_to_me"); }
            var task = (short)ContentDatabase.HandleOf(world.Content.Actions, u.RequestTask, d => d.Id);
            if (task < 0) { task = conv.PrevAction >= 0 ? conv.PrevAction : (short)ContentDatabase.HandleOf(world.Content.Actions, "action.gather_food", d => d.Id); }
            var hours = u.RequestHours > 0f ? Math.Clamp(u.RequestHours, 0.25f, 8f) : 1f;
            Track(world, conv, world.Decisions.Open(Social.RequestOwner.Id, new DpContext(Social.RequestOwner.Kind, conv.Npc, conv.Player, Social.RequestOwner.Pack(task, hours, lWords)),
                decider, DecisionRulesEngine.ConversationDeadlineSteps));
        }

        if (npc >= 0 && player >= 0 && u.Act == "tell" && world.Content.ClaimHandle(u.ClaimPredicate) is var predicate and >= 0 && !u.ClaimSubject.IsNone)
        {
            // 16 §7.10: the claim is fixed first and checked against ground truth; a detected lie is an act, not a DP.
            var claim = world.Claims.Intern(new Social.Claim
            {
                Predicate = (ushort)predicate, Subject = u.ClaimSubject.Value, Object = u.ClaimObject.Value, Magnitude = 1f, TimeMin = -1,
                DerivedFrom = -1, Qualifiers = Social.ClaimQualifiers.Blurred,
            });
            var (lie, caught) = Social.BeingToldOwner.LieTest(world, npc, player, claim);
            if (lie && caught)
            {
                var me = world.People.Ids[npc];
                world.Relationships.ApplyModifier(me, conv.Player, "opinion.lied_to_me");
                world.Relationships.TrustEvidence(me, conv.Player, -15f);
                world.Emit(Salience.Minor, me, new LieCaught(me, conv.Player, claim));
            }
            else
            {
                Track(world, conv, world.Decisions.Open(Social.BeingToldOwner.Id, new DpContext(Social.BeingToldOwner.Kind, conv.Npc, conv.Player, Social.BeingToldOwner.Pack(claim, u.ClaimFirstHand, lWords)),
                    decider, DecisionRulesEngine.ConversationDeadlineSteps));
            }
        }

        // 16 §4.15: a rapport DP after every 8 player turns (the close brings the last; at most 3 in all).
        if (conv.Turn % Social.RapportOwner.EveryTurns == 0 && conv.RapportCount < Social.RapportOwner.MaxPerConversation - 1)
        {
            conv.RapportCount++;
            Track(world, conv, world.Decisions.Open(Social.RapportOwner.Id, new DpContext(Social.RapportOwner.Kind, conv.Npc, conv.Player, Social.RapportOwner.Encode(conv.MeanWords)),
                decider, DecisionRulesEngine.ConversationDeadlineSteps));
        }

        var id = world.Decisions.Open(InitiativeOwner.Id, new DpContext(InitiativeOwner.Kind, conv.Npc, conv.Player, (long)conv.Id), decider,
            DecisionRulesEngine.ConversationDeadlineSteps);
        if (world.Decisions.IsOpen(id)) { conv.Dps.Add(id); }
    }
}
