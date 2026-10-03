using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
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
    private long _eventSeq;

    public SimWorld(ulong worldSeed, long startGameMs = 0, int dayLengthMinutes = SimClock.DefaultDayLengthMinutes)
    {
        WorldSeed = worldSeed;
        Clock = new SimClock(startGameMs, dayLengthMinutes);
    }

    public ulong WorldSeed { get; }
    public SimClock Clock { get; }
    public EntityIdAllocator Ids { get; } = new();
    public PersonTable People { get; } = new();

    /// <summary>Compiled content (definitions and tuning). Its hash is recorded in saves and run outputs.</summary>
    public Content.ContentDatabase Content { get; set; } = FeudalSim.Sim.Content.ContentDatabase.Empty;

    /// <summary>Scheduler for parallel phases; Hosting swaps in a threaded one. Must not change results.</summary>
    public IJobScheduler Jobs { get; set; } = SerialJobScheduler.Instance;

    /// <summary>Hash the state after every Nth step (0 = never). Hashing is read-only.</summary>
    public int HashEveryNSteps { get; set; }

    public SimWorld AddSystem(ISimSystem system)
    {
        _systems.Add(system);
        return this;
    }

    /// <summary>Queues a command for the next step. <c>Seq</c> is assigned by the host and must increase.</summary>
    public void Enqueue(in CommandEnvelope command)
    {
        if (_pending.Count > 0 && command.Seq <= _pending[^1].Seq)
        {
            throw new InvalidOperationException("Command Seq must increase monotonically.");
        }

        _pending.Add(command);
    }

    /// <summary>Last issued event <c>Seq</c>; persisted so numbering continues across save/load.</summary>
    public long EventSeq => _eventSeq;

    internal void RestoreEventSeq(long seq) => _eventSeq = seq;

    public void Emit(Salience salience, EntityId primary, DomainEvent payload)
        => _events.Add(new EventEnvelope(++_eventSeq, Clock.Step, Clock.GameMinute, salience, primary, payload));

    public StepOutput Step()
    {
        // Phase 0 — Begin.
        var previousDay = Clock.GameMs / SimClock.MsPerGameDay;
        var dt = Clock.AdvanceFineStep();
        var ctx = new StepContext(WorldSeed, Clock.Step, Clock.GameMs, dt, SimClock.StepMs);
        var newDay = Clock.GameMs / SimClock.MsPerGameDay;
        if (newDay != previousDay)
        {
            Emit(Salience.Minor, EntityId.None, new DayStarted(newDay));
        }

        // Phase 1 — Commands, in Seq order.
        var applied = ApplyCommands(ctx);

        // Phases 2–5 — systems.
        foreach (var phase in (ReadOnlySpan<SimPhase>)[SimPhase.Sense, SimPhase.Decide, SimPhase.Resolve, SimPhase.World])
        {
            foreach (var system in _systems)
            {
                if (system.Phase == phase) { system.Run(ctx, this); }
            }
        }

        // Phase 7 — Post.
        var events = _events.ToArray();
        _events.Clear();
        var hash = HashEveryNSteps > 0 && Clock.Step % HashEveryNSteps == 0 ? StateHasher.Hash(this) : 0UL;
        return new StepOutput { Step = Clock.Step, GameMs = Clock.GameMs, AppliedCommands = applied, Events = events, StateHash = hash };
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
                if (string.IsNullOrWhiteSpace(c.Name) || !float.IsFinite(c.X) || !float.IsFinite(c.Z))
                {
                    Reject(command, "Invalid spawn.");
                    break;
                }

                var id = Ids.Next(EntityKind.Person);
                People.Add(id, c.Name, new PersonCore { BirthGameMinute = Clock.GameMinute },
                    new Transform { X = c.X, Z = c.Z }, Needs.Full);
                Emit(Salience.Minor, id, new PersonSpawned(id, c.Name));
                break;

            default:
                Reject(command, $"Unknown command {command.Payload?.GetType().Name ?? "null"}.");
                break;
        }
    }

    private void Reject(in CommandEnvelope command, string reason)
        => Emit(Salience.Trace, EntityId.None, new CommandRejected(command.Seq, reason));
}
