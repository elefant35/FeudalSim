using FeudalSim.Sim.Ai;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Time;
using FeudalSim.Sim.World;

namespace FeudalSim.Sim;

/// <summary>
/// The simulation: state tables, clock and the phase pipeline. Advances only through
/// <see cref="Step"/>, changes only through logged <see cref="StateCommand"/>s, and is a pure
/// function of (world seed, start time, command log).
/// </summary>
public sealed class SimWorld
{
    private readonly List<ISimSystem> _systems = [];
    private readonly List<CommandEnvelope> _pending = [];
    private readonly List<EventEnvelope> _events = [];
    private readonly List<AiRequest> _aiOutbox = [];
    private readonly SortedDictionary<long, AiRequest> _aiPending = [];   // sorted: deterministic deadline sweep
    private long _aiRequestSeq;
    private long _eventSeq;

    public SimWorld(ulong worldSeed, long startGameMs = 0, int dayLengthMinutes = SimClock.DefaultDayLengthMinutes)
    {
        WorldSeed = worldSeed;
        Clock = new SimClock(startGameMs, dayLengthMinutes);
        Decisions = new DecisionRulesEngine(this);
        Relationships = new Social.RelationshipStore(this);
        Reputation = new Social.ReputationStore(this);
    }

    public ulong WorldSeed { get; }
    public SimClock Clock { get; }
    public EntityIdAllocator Ids { get; } = new();
    public PersonTable People { get; } = new();

    /// <summary>Episodic memories (16 §6).</summary>
    public Social.MemoryStore Memories { get; } = new();

    /// <summary>Interned claims (16 §7.1); ground truth via observed events.</summary>
    public Social.ClaimStore Claims { get; } = new();

    /// <summary>Beliefs per person (16 §7).</summary>
    public Social.BeliefStore Beliefs { get; } = new();

    /// <summary>Reputation and Renown (16 §8).</summary>
    public Social.ReputationStore Reputation { get; }

    /// <summary>Relationships (16 §4): opinion, trust, familiarity, fear, tags.</summary>
    public Social.RelationshipStore Relationships { get; }

    /// <summary>Coin and goods (M1 prototype of 15's purses and inventories).</summary>
    public Economy.Holdings Holdings { get; } = new();

    /// <summary>Open haggles with the player (15 §5).</summary>
    public Economy.NegotiationStore Negotiations { get; } = new();

    /// <summary>Favors people agreed to do (16 §5.4), carried out by the utility AI.</summary>
    public Social.FavorStore Favors { get; } = new();

    /// <summary>Open quarrels on the escalation ladder (16 §9).</summary>
    public Social.ConfrontationStore Confrontations { get; } = new();

    /// <summary>Conversations with the player (21 §14.4, 22 §4.11).</summary>
    public Dialogue.ConversationStore Conversations { get; } = new();

    /// <summary>The M1 graybox camp: places and shared stocks (inactive in M0 scenarios).</summary>
    public CampRecord Camp;

    /// <summary>Decision points: menus, guards, the policy and the DP watchdog (canon §13.1, 22 §6).</summary>
    public DecisionRulesEngine Decisions { get; }

    /// <summary>The player's last reported pose (set by logged <see cref="PlayerMoved"/> commands).</summary>
    public PlayerState Player;

    /// <summary>The player's own Person row, if spawned (<see cref="PersonFlags.Player"/>); derived from the flag on load.</summary>
    public EntityId PlayerId { get; private set; }

    /// <summary>Row of the player's character, or −1.</summary>
    public int PlayerRow => PlayerId.IsNone ? -1 : People.IndexOf(PlayerId);

    /// <summary>True for the player's character: AI, psychology and NPC↔NPC systems skip it (21 §16).</summary>
    public bool IsPlayer(int row) => (People.Core[row].Flags & PersonFlags.Player) != 0;

    internal void RestorePlayerId()
    {
        PlayerId = EntityId.None;
        for (var i = 0; i < People.Count; i++) { if (IsPlayer(i)) { PlayerId = People.Ids[i]; } }
    }

    /// <summary>Set by <see cref="HoldAiRequests"/> while a save waits; not saved (a restored world is never holding).</summary>
    public bool AiHeld { get; private set; }

    /// <summary>Compiled content (definitions and tuning). Its hash is recorded in saves and run outputs.</summary>
    public Content.ContentDatabase Content { get; set; } = FeudalSim.Sim.Content.ContentDatabase.Empty;

    /// <summary>Scheduler for parallel phases; Hosting swaps in a threaded one. Must not change results.</summary>
    public IJobScheduler Jobs { get; set; } = SerialJobScheduler.Instance;

    /// <summary>Hash the state after every Nth step (0 = never). Hashing is read-only.</summary>
    public int HashEveryNSteps { get; set; }

    /// <summary>Who updates this step and over how much game time (LOD cadences, 20 §5.2). Rebuilt every step.</summary>
    public TierSchedule Due { get; } = new();

    /// <summary>Optional timing hook for benchmarks (Hosting implements it; the sim never reads a clock).</summary>
    public ISystemObserver? Observer { get; set; }

    /// <summary>Registered systems (configuration, in registration order) — for metrics and tooling.</summary>
    public IReadOnlyList<ISimSystem> Systems => _systems;

    public SimWorld AddSystem(ISimSystem system)
    {
        _systems.Add(system);
        return this;
    }

    /// <summary>Queues a command for the next step. <c>Seq</c> is assigned by the host and must increase.</summary>
    public void Enqueue(in CommandEnvelope command)
    {
        if (command.Seq <= LastCommandSeq)
        {
            throw new InvalidOperationException($"Command Seq must increase monotonically ({command.Seq} after {LastCommandSeq}).");
        }

        LastCommandSeq = command.Seq;
        _pending.Add(command);
    }

    /// <summary>Highest command <c>Seq</c> ever enqueued; hosts continue numbering from here.</summary>
    public long LastCommandSeq { get; private set; }

    internal void RestoreLastCommandSeq(long seq) => LastCommandSeq = seq;

    /// <summary>Last issued event <c>Seq</c>; persisted so numbering continues across save/load.</summary>
    public long EventSeq => _eventSeq;

    internal void RestoreEventSeq(long seq) => _eventSeq = seq;

    public void Emit(Salience salience, EntityId primary, DomainEvent payload)
        => _events.Add(new EventEnvelope(++_eventSeq, Clock.Step, Clock.GameMinute, salience, primary, payload));

    public StepOutput Step() => Step(0);

    /// <summary>
    /// One macro step of <paramref name="gameMs"/> (20 §5.4: skips advance 1 game hour or 1 game day per step). Every
    /// due row integrates the whole interval, so macro steps are meant for LOD3 populations (skips, headless years).
    /// </summary>
    public StepOutput StepMacro(long gameMs) => Step(gameMs);

    private StepOutput Step(long macroGameMs)
    {
        // Phase 0 — Begin.
        var previousDay = Clock.GameMs / SimClock.MsPerGameDay;
        var dt = macroGameMs > 0 ? Clock.AdvanceMacroStep(macroGameMs) : Clock.AdvanceFineStep();
        var ctx = new StepContext(WorldSeed, Clock.Step, Clock.GameMs, dt, SimClock.StepMs);
        var newDay = Clock.GameMs / SimClock.MsPerGameDay;
        if (newDay != previousDay)
        {
            Emit(Salience.Minor, EntityId.None, new DayStarted(newDay));
        }

        // Phase 1 — Commands, in Seq order, then the AI and decision-point deadline sweeps.
        var applied = ApplyCommands(ctx);
        SweepAiDeadlines(ctx.Step);
        Decisions.SweepDeadlines(ctx.Step);

        // Phases 2–5 — systems.
        foreach (var phase in (ReadOnlySpan<SimPhase>)[SimPhase.Sense, SimPhase.Decide, SimPhase.Resolve, SimPhase.World])
        {
            if (phase == SimPhase.Decide) { Due.Build(this, ctx.GameMs, ctx.DtGameMs); }   // after LOD changes
            for (var s = 0; s < _systems.Count; s++)
            {
                var system = _systems[s];
                if (system.Phase != phase) { continue; }
                Observer?.Begin(s);
                system.Run(ctx, this);
                Observer?.End(s);
            }
        }

        // Phase 7 — Post.
        var events = _events.ToArray();
        _events.Clear();
        var outbox = _aiOutbox.ToArray();
        _aiOutbox.Clear();
        var opened = Decisions.DrainOutbox();
        var hash = HashEveryNSteps > 0 && Clock.Step % HashEveryNSteps == 0 ? StateHasher.Hash(this) : 0UL;
        return new StepOutput
        {
            Step = Clock.Step, GameMs = Clock.GameMs, AppliedCommands = applied, Events = events, AiRequests = outbox,
            OpenedDecisions = opened, StateHash = hash,
        };
    }

    private CommandEnvelope[] ApplyCommands(in StepContext ctx)
    {
        if (_pending.Count == 0) { return []; }
        var applied = new CommandEnvelope[_pending.Count];
        for (var i = 0; i < _pending.Count; i++)
        {
            var command = _pending[i] with { ApplyStep = ctx.Step };
            applied[i] = command;
            Apply(command);
        }

        _pending.Clear();
        return applied;
    }

    private void Apply(in CommandEnvelope command)
    {
        switch (command.Payload)
        {
            case SetDayLength c:
                if (Clock.TrySetDayLength(c.Minutes)) { Emit(Salience.Minor, EntityId.None, new DayLengthChanged(c.Minutes)); }
                else { Reject(command, $"Day length {c.Minutes} is not one of the allowed values."); }
                break;

            case SpawnPerson c:
                if (c.Name is null || !float.IsFinite(c.X) || !float.IsFinite(c.Z))
                {
                    Reject(command, "Invalid spawn.");
                    break;
                }

                if (c.AgeYears is < 0 or > 120 || (c.Profession is { } pid && Content.Professions.Count > 0 && Content.ProfessionHandle(pid) < 0))
                {
                    Reject(command, "Invalid spawn (age or profession).");
                    break;
                }

                if (c.IsPlayer && !PlayerId.IsNone)
                {
                    Reject(command, "The player's character already exists.");
                    break;
                }

                var id = Ids.Next(EntityKind.Person);
                var newRow = People.Add(id, c.Name, new PersonCore { BirthGameMinute = Clock.GameMinute },
                    new Transform { X = c.X, Z = c.Z }, Needs.Full);
                var culture = Content.CultureHandle(c.Culture ?? "culture.varrow");
                var profession = c.Profession is null ? -1 : Content.ProfessionHandle(c.Profession);
                var age = PersonGenerator.Generate(this, newRow, culture < 0 ? Personality.None : (ushort)culture,
                    profession < 0 ? Personality.None : (ushort)profession, c.AgeYears);
                var name = string.IsNullOrWhiteSpace(c.Name) ? PersonGenerator.Name(this, id, culture, People.Count) : c.Name;
                People.Rename(newRow, name);
                People.Core[newRow].BirthGameMinute = Clock.GameMinute - (age * GameDate.MinutesPerYear);
                People.Lod[newRow].LastUpdateGameMs = Clock.GameMs - Clock.GameMsPerStep;   // first update integrates one step
                if (c.IsPlayer)
                {
                    People.Core[newRow].Flags |= PersonFlags.Player;
                    People.Lod[newRow].Tier = LodTier.Lod0;   // the player is always embodied
                    People.Lod[newRow].Embodied = true;
                    PlayerId = id;
                }

                Emit(Salience.Minor, id, new PersonSpawned(id, name));
                break;

            case SetLodTier c:
            {
                var row = People.IndexOf(c.Person);
                if (row < 0 || c.Tier is not (LodTier.Lod1 or LodTier.Lod2 or LodTier.Lod3) || People.Lod[row].Tier == LodTier.Lod0)
                {
                    Reject(command, $"Cannot set {c.Person} to {c.Tier}.");
                    break;
                }

                if (People.Lod[row].Tier != c.Tier)
                {
                    Emit(Salience.Trace, c.Person, new LodChanged(c.Person, People.Lod[row].Tier, c.Tier));
                    People.Lod[row].Tier = c.Tier;
                }

                break;
            }

            case SetHoldings c:
            {
                var row = People.IndexOf(c.Person);
                var item = c.Item.Length == 0 ? -1 : FeudalSim.Sim.Content.ContentDatabase.HandleOf(Content.Items, c.Item, i => i.Id);
                if (row < 0 || (c.Item.Length > 0 && item < 0) || c.Qty < 0 || c.CoinF < 0) { Reject(command, "Invalid holdings."); break; }
                Holdings.Set(c.Person, item, c.Qty, c.CoinF);
                break;
            }

            case TradeOpen c:
                Economy.TradeOwner.Open(this, command, c);
                break;

            case TradeOffer c:
                Economy.TradeOwner.Offer(this, command, c);
                break;

            case TradeAccept c:
                Economy.TradeOwner.AcceptNpcOffer(this, command, c);
                break;

            case TradeWalkAway c:
                Economy.TradeOwner.WalkAway(this, command, c);
                break;

            case StartConversation c:
                Dialogue.ConversationSystem.Start(this, command, c);
                break;

            case EndConversation c:
                Dialogue.ConversationSystem.EndByPlayer(this, command, c);
                break;

            case DialogueLineRendered c:
                Dialogue.ConversationSystem.Line(this, command, c);
                break;

            case PlayerUtteranceClassified c:
                Dialogue.ConversationSystem.Utterance(this, command, c);
                break;

            case HoldAiRequests c:
                AiHeld = c.Hold;
                break;

            case PlayerMoved c:
                if (!float.IsFinite(c.X) || !float.IsFinite(c.Z)) { Reject(command, "Invalid player pose."); break; }
                Player = new PlayerState { Present = true, X = c.X, Z = c.Z, Yaw = c.Yaw };
                if (PlayerRow is var pr and >= 0)
                {
                    ref var pt = ref People.Transforms[pr];
                    (pt.X, pt.Z, pt.Yaw) = (c.X, c.Z, c.Yaw);
                }

                break;

            case EmbodimentReport c:
            {
                var row = People.IndexOf(c.Person);
                if (row < 0 || People.Lod[row].Tier != LodTier.Lod0 || !float.IsFinite(c.X) || !float.IsFinite(c.Z))
                {
                    Reject(command, $"Embodiment report for {c.Person}, which is not embodied.");
                    break;
                }

                ref var t = ref People.Transforms[row];
                ref var l = ref People.Lod[row];
                if (!l.Embodied)
                {
                    var dx = c.X - t.X;
                    var dz = c.Z - t.Z;
                    l.Embodied = true;
                    Emit(Salience.Trace, c.Person, new Embodied(c.Person, MathF.Sqrt((dx * dx) + (dz * dz))));
                }

                t.X = c.X;
                t.Z = c.Z;
                t.Yaw = c.Yaw;
                break;
            }

            case AiResultCommand c:
                if (!_aiPending.TryGetValue(c.RequestId, out var pending))
                {
                    Reject(command, $"Late or unknown AI result {c.RequestId}.");
                    break;
                }

                _aiPending.Remove(c.RequestId);
                var ok = c.Outcome == AiOutcome.Ok && !string.IsNullOrWhiteSpace(c.Text);
                Emit(Salience.Trace, pending.Speaker, new AiResultApplied(c.RequestId, !ok, ok ? c.Text : pending.FallbackText, ok ? c.ProviderTag : "fallback"));
                break;

            case DecisionPointOpened c:
                Decisions.VerifyOpened(command, c);
                break;

            case DecisionMade c:
                Decisions.ApplyDecision(command, c);
                break;

            default:
                Reject(command, $"Unknown command {command.Payload?.GetType().Name ?? "null"}.");
                break;
        }
    }

    /// <summary>
    /// Asks the AI gateway for something. The sim never waits: if no valid result is applied by
    /// <c>now + deadlineSteps</c>, the fallback text is used and a later result is rejected (20 §11).
    /// </summary>
    public long RequestAi(AiTaskKind kind, AiPriority priority, int deadlineSteps, EntityId speaker, EntityId listener, string context, string fallbackText)
    {
        var request = new AiRequest(++_aiRequestSeq, kind, priority, Clock.Step, Clock.Step + deadlineSteps, speaker, listener, context, fallbackText);
        if (AiHeld)
        {
            Emit(Salience.Trace, speaker, new AiResultApplied(request.RequestId, true, fallbackText, "fallback:save"));
            return request.RequestId;
        }

        _aiPending.Add(request.RequestId, request);
        _aiOutbox.Add(request);
        return request.RequestId;
    }

    public int PendingAiRequests => _aiPending.Count;

    /// <summary>Pending AI requests (saved, hashed) and the last issued request id.</summary>
    internal (AiRequest[] Pending, long Seq) ExportAi() => ([.. _aiPending.Values], _aiRequestSeq);

    internal void ImportAi(AiRequest[] pending, long seq)
    {
        _aiPending.Clear();
        foreach (var r in pending) { _aiPending.Add(r.RequestId, r); }
        _aiRequestSeq = seq;
    }

    internal void HashAiInto(System.IO.Hashing.XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        BitConverter.TryWriteBytes(b, _aiRequestSeq);
        h.Append(b);
        foreach (var (id, r) in _aiPending)
        {
            BitConverter.TryWriteBytes(b, id);
            h.Append(b);
            BitConverter.TryWriteBytes(b, r.DeadlineStep);
            h.Append(b);
        }
    }

    public bool IsAiPending(long requestId) => _aiPending.ContainsKey(requestId);

    /// <summary>True if a request of this kind is in flight (state: survives save/load, unlike a system's memory of an id).</summary>
    public bool IsAiPending(AiTaskKind kind)
    {
        foreach (var r in _aiPending.Values) { if (r.Kind == kind) { return true; } }
        return false;
    }

    private void SweepAiDeadlines(long step)
    {
        if (_aiPending.Count == 0) { return; }
        List<long>? expired = null;
        foreach (var (id, request) in _aiPending)
        {
            if (request.DeadlineStep <= step) { (expired ??= []).Add(id); }
        }

        if (expired is null) { return; }
        foreach (var id in expired)
        {
            var request = _aiPending[id];
            _aiPending.Remove(id);
            Emit(Salience.Trace, request.Speaker, new AiResultApplied(id, true, request.FallbackText, "fallback:deadline"));
        }
    }

    internal void RejectCommand(in CommandEnvelope command, string reason) => Reject(command, reason);

    private void Reject(in CommandEnvelope command, string reason)
        => Emit(Salience.Trace, EntityId.None, new CommandRejected(command.Seq, reason));
}
