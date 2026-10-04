using System.IO.Hashing;
using System.Text;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Events;
using FeudalSim.Sim.Time;

namespace FeudalSim.Sim.Decisions;

/// <summary>
/// The sim side of the Dialogue Rules Engine (canon §13.1, 20 §11, 22 §6): opens decision points from
/// owner-built menus, draws the policy's pick at open, guards a decider's choice at commit against the rebuilt
/// menu, resolves undecided DPs at their deadline, and hands the chosen option to its owner to execute.
/// Deterministic: sorted containers, keyed RNG, no wall clock. The policy itself is never guarded — it *is*
/// the calibrated expectation and samples every eligible option at its p_i (22 §6.6).
/// </summary>
public sealed class DecisionRulesEngine
{
    public const float FloorLowMedium = 0.02f;      // canon §13.1 step 4
    public const float FloorHighCritical = 0.05f;
    public const float LongShotBelow = 0.20f;
    public const float CriticalMin = 0.25f;
    public const int LongShotsPerPairPerDay = 2;
    public const int ConversationDeadlineSteps = 40;   // 4 s of embodied time (20 §11)
    public const int FastDeadlineSteps = 5;            // 0.5 s

    private readonly SimWorld _world;
    private readonly SortedDictionary<string, IDecisionPointOwner> _owners = new(StringComparer.Ordinal);

    // State: hashed by StateHasher. Not yet saved — persisted together with the pending AI requests in M1
    // (33-progress discovered work); until then a save taken while a DP is open loses it.
    private readonly SortedDictionary<ulong, DecisionPoint> _open = [];
    private readonly SortedDictionary<(ulong Chooser, ulong Counterpart), (long Day, int Used)> _longShots = [];
    private long _ordinalStep = -1;
    private int _ordinal;

    private readonly List<DecisionPointOpened> _outbox = [];

    internal DecisionRulesEngine(SimWorld world) => _world = world;

    public int OpenCount => _open.Count;

    public bool IsOpen(ulong id) => _open.ContainsKey(id);

    /// <summary>The policy's pre-drawn pick for an open DP (tests, debug overlay); null when not open.</summary>
    public string? PolicyChoiceOf(ulong id) => _open.TryGetValue(id, out var dp) ? dp.PolicyChoice : null;

    /// <summary>Owners are configuration (like systems), registered when the world is built.</summary>
    public void Register(IDecisionPointOwner owner) => _owners.Add(owner.OwnerId, owner);

    /// <summary>
    /// Opens a DP. With <paramref name="maxDecider"/> = Policy (off-screen, NPC↔NPC, headless) it resolves at once.
    /// Otherwise it waits for a <see cref="DecisionMade"/> until <c>now + deadlineSteps</c>, then the policy decides.
    /// </summary>
    public ulong Open(string ownerId, in DpContext context, DeciderKind maxDecider, int deadlineSteps)
    {
        if (!_owners.TryGetValue(ownerId, out var owner)) { throw new InvalidOperationException($"No decision-point owner '{ownerId}' is registered."); }
        ArgumentOutOfRangeException.ThrowIfLessThan(deadlineSteps, 1);
        var menu = Canonical(owner.BuildMenu(_world, context));
        var step = _world.Clock.Step;
        if (_ordinalStep != step) { (_ordinalStep, _ordinal) = (step, 0); }
        var id = SplitMix64.Mix((ulong)step, context.Chooser.Value, OwnerKey(ownerId), (ulong)_ordinal++, 0xD9);   // 20 §8.5
        var policy = SamplePolicy(menu, context.Chooser, id, Salt.DecisionPolicy)
                     ?? throw new InvalidOperationException($"Decision point {ownerId}/{context.Kind} has no eligible option.");
        var ctx = context;
        var preCleared = menu.Where(o => Check(o, ctx) == GuardOutcome.Passed).Select(o => o.Id).ToArray();
        var dp = new DecisionPoint
        {
            Id = id, Owner = ownerId, Context = context, Menu = menu, MenuHash = HashMenu(menu), PreCleared = preCleared,
            MaxDecider = maxDecider, OpenStep = step, DeadlineStep = step + deadlineSteps, PolicyChoice = policy.Id,
        };

        if (maxDecider == DeciderKind.Policy || preCleared.Length == 0)
        {
            Resolve(dp, owner, policy, DeciderKind.Policy, GuardOutcome.Inline, null, "policy");
            return id;
        }

        _open.Add(id, dp);
        _outbox.Add(new DecisionPointOpened(id, ownerId, context, dp.MenuHash, menu, preCleared, step, dp.DeadlineStep, maxDecider));
        return id;
    }

    /// <summary>Closes a DP early (conversation ended, P0 interrupt; 21 §14.6). A later decision is rejected.</summary>
    public bool Cancel(ulong id, string reason)
    {
        if (!_open.Remove(id, out var dp)) { return false; }
        _world.Emit(Salience.Trace, dp.Context.Chooser, new DecisionPointCancelled(id, reason));
        return true;
    }

    /// <summary>The guard G (22 §6.6): feasibility and bounds, not taste. Pure apart from reading the long-shot budget.</summary>
    public GuardOutcome Check(MenuOption? option, in DpContext context)
    {
        if (option is null) { return GuardOutcome.OffMenu; }
        if (!option.Eligible) { return GuardOutcome.Ineligible; }
        if (option.P < (option.Stakes >= Stakes.High ? FloorHighCritical : FloorLowMedium)) { return GuardOutcome.BelowFloor; }
        if (IsLongShot(option) && LongShotsUsed(context.Chooser, context.Counterpart) >= LongShotsPerPairPerDay) { return GuardOutcome.LongShotBudget; }
        if (option.Stakes == Stakes.Critical && option.PCrit < CriticalMin) { return GuardOutcome.CriticalCheck; }
        return GuardOutcome.Passed;
    }

    public int LongShotsUsed(EntityId chooser, EntityId counterpart)
        => _longShots.TryGetValue((chooser.Value, counterpart.Value), out var c) && c.Day == Today() ? c.Used : 0;

    internal DecisionPointOpened[] DrainOutbox()
    {
        if (_outbox.Count == 0) { return []; }
        var opened = _outbox.ToArray();
        _outbox.Clear();
        return opened;
    }

    /// <summary>Phase 1: a decider's answer. Re-runs the guard authoritatively against the rebuilt menu.</summary>
    internal void ApplyDecision(in CommandEnvelope command, DecisionMade made)
    {
        if (!_open.Remove(made.Id, out var dp))
        {
            _world.RejectCommand(command, $"Late or unknown decision {made.Id:x16}.");
            return;
        }

        var owner = _owners[dp.Owner];
        var menu = Canonical(owner.BuildMenu(_world, dp.Context));
        if (made.MenuHash != dp.MenuHash) { ResolveByPolicy(dp, owner, menu, GuardOutcome.StaleMenu, made.Choice, made.ProviderTag); return; }
        if (made.Choice is null) { ResolveByPolicy(dp, owner, menu, GuardOutcome.ModePolicy, null, made.ProviderTag); return; }

        var option = Find(menu, made.Choice);
        var guard = Check(option, dp.Context);
        if (guard != GuardOutcome.Passed) { ResolveByPolicy(dp, owner, menu, guard, made.Choice, made.ProviderTag); return; }
        if (IsLongShot(option!)) { ConsumeLongShot(dp.Context); }   // only executed model choices spend the budget
        Resolve(dp, owner, option!, made.Decider, GuardOutcome.Passed, null, made.ProviderTag);
    }

    /// <summary>Phase 1, after commands: the DP watchdog. Undecided DPs at their deadline take the policy's pick.</summary>
    internal void SweepDeadlines(long step)
    {
        if (_open.Count == 0) { return; }
        List<DecisionPoint>? due = null;
        foreach (var dp in _open.Values)
        {
            if (dp.DeadlineStep <= step) { (due ??= []).Add(dp); }
        }

        if (due is null) { return; }
        foreach (var dp in due)
        {
            _open.Remove(dp.Id);
            var owner = _owners[dp.Owner];
            ResolveByPolicy(dp, owner, Canonical(owner.BuildMenu(_world, dp.Context)), GuardOutcome.Deadline, null, "policy:deadline");
        }
    }

    /// <summary>Phase 1: checks a logged <see cref="DecisionPointOpened"/> against the DP the sim itself opened.</summary>
    internal void VerifyOpened(in CommandEnvelope command, DecisionPointOpened record)
    {
        string? problem = null;
        if (!_open.TryGetValue(record.Id, out var dp)) { problem = "the sim has no such open decision point"; }
        else if (dp.MenuHash != record.MenuHash || HashMenu(record.Options) != record.MenuHash) { problem = "menu hash differs"; }
        else if (dp.Owner != record.Owner || dp.Context != record.Context || dp.DeadlineStep != record.DeadlineStep || dp.MaxDecider != record.MaxDecider)
        {
            problem = "owner, context, deadline or decider differs";
        }

        if (problem is not null)
        {
            _world.Emit(Salience.Notable, EntityId.None, new IntegrityMismatch(command.Seq, $"DecisionPointOpened {record.Id:x16}: {problem}"));
        }
    }

    internal void HashInto(XxHash64 h)
    {
        Span<byte> b = stackalloc byte[8];
        foreach (var dp in _open.Values)
        {
            Append(h, b, (long)dp.Id);
            Append(h, b, (long)dp.MenuHash);
            Append(h, b, dp.OpenStep);
            Append(h, b, dp.DeadlineStep);
            Append(h, b, (long)dp.MaxDecider);
            AppendString(h, b, dp.PolicyChoice);
        }

        foreach (var ((chooser, counterpart), (day, used)) in _longShots)
        {
            Append(h, b, (long)chooser);
            Append(h, b, (long)counterpart);
            Append(h, b, day);
            Append(h, b, used);
        }

        Append(h, b, _ordinalStep);
        Append(h, b, _ordinal);
    }

    /// <summary>XxHash64 of the canonical menu: options in id order, sorted params, P and PCrit quantized to 1e-4 (20 §11).</summary>
    public static ulong HashMenu(IReadOnlyList<MenuOption> canonicalMenu)
    {
        var h = new XxHash64();
        Span<byte> b = stackalloc byte[8];
        foreach (var o in canonicalMenu)
        {
            AppendString(h, b, o.Id);
            AppendString(h, b, o.Family);
            Append(h, b, o.Params.Length);
            foreach (var p in o.Params)
            {
                AppendString(h, b, p.Key);
                Append(h, b, p.Value);
            }

            Append(h, b, o.Eligible ? 1 : 0);
            Append(h, b, (long)MathF.Round(o.P * 10_000f));
            Append(h, b, (long)MathF.Round(o.PCrit * 10_000f));
            Append(h, b, (long)o.Stakes);
            Append(h, b, o.FavorsPlayer ? 1 : 0);
        }

        return h.GetCurrentHashAsUInt64();
    }

    /// <summary>Options sorted by id (ordinal) with params sorted by key; ids must be unique.</summary>
    public static MenuOption[] Canonical(IReadOnlyList<MenuOption> menu)
    {
        var sorted = menu.Select(o => o.Params.Length > 1 ? o with { Params = [.. o.Params.OrderBy(p => p.Key, StringComparer.Ordinal)] } : o)
                         .OrderBy(o => o.Id, StringComparer.Ordinal).ToArray();
        for (var i = 1; i < sorted.Length; i++)
        {
            if (string.Equals(sorted[i].Id, sorted[i - 1].Id, StringComparison.Ordinal)) { throw new InvalidOperationException($"Duplicate menu option '{sorted[i].Id}'."); }
        }

        return sorted;
    }

    private void ResolveByPolicy(DecisionPoint dp, IDecisionPointOwner owner, MenuOption[] menu, GuardOutcome guard, string? rejected, string providerTag)
    {
        // Re-validation (21 §7.8): if the pre-drawn pick has become ineligible, re-sample the still-eligible options.
        var chosen = Find(menu, dp.PolicyChoice) is { Eligible: true } pre ? pre : SamplePolicy(menu, dp.Context.Chooser, dp.Id, Salt.DecisionPolicyResample);
        if (chosen is null)
        {
            _world.Emit(Salience.Minor, dp.Context.Chooser, new DecisionResolved(dp.Id, dp.Owner, dp.Context.Chooser, "", DeciderKind.Policy, guard, rejected, providerTag));
            return;
        }

        Resolve(dp, owner, chosen, DeciderKind.Policy, guard, rejected, providerTag);
    }

    private void Resolve(DecisionPoint dp, IDecisionPointOwner owner, MenuOption chosen, DeciderKind decider, GuardOutcome guard, string? rejected, string providerTag)
    {
        _world.Emit(Salience.Minor, dp.Context.Chooser, new DecisionResolved(dp.Id, dp.Owner, dp.Context.Chooser, chosen.Id, decider, guard, rejected, providerTag));
        owner.Execute(_world, dp, chosen);
    }

    private MenuOption? SamplePolicy(MenuOption[] menu, EntityId chooser, ulong dpId, uint salt)
    {
        var total = 0f;
        var eligible = 0;
        foreach (var o in menu)
        {
            if (!o.Eligible) { continue; }
            total += Math.Max(0f, o.P);
            eligible++;
        }

        if (eligible == 0) { return null; }
        var rng = new Rng(SplitMix64.Mix(_world.WorldSeed, (ulong)RngStream.Ai, chooser.Value, dpId, salt));   // 20 §8.2
        var r = rng.NextFloat01();
        if (total <= 0f)
        {
            var k = (int)(r * eligible);   // no propensities given: uniform over eligible
            foreach (var o in menu) { if (o.Eligible && k-- == 0) { return o; } }
        }

        var x = r * total;
        MenuOption? last = null;
        foreach (var o in menu)
        {
            if (!o.Eligible) { continue; }
            last = o;
            x -= Math.Max(0f, o.P);
            if (x < 0f) { return o; }
        }

        return last;
    }

    private void ConsumeLongShot(in DpContext context)
    {
        var key = (context.Chooser.Value, context.Counterpart.Value);
        var today = Today();
        _longShots[key] = _longShots.TryGetValue(key, out var c) && c.Day == today ? (today, c.Used + 1) : (today, 1);
    }

    private static bool IsLongShot(MenuOption o) => o.FavorsPlayer && o.P < LongShotBelow;

    private long Today() => _world.Clock.GameMs / SimClock.MsPerGameDay;

    private static MenuOption? Find(MenuOption[] menu, string id)
    {
        foreach (var o in menu) { if (string.Equals(o.Id, id, StringComparison.Ordinal)) { return o; } }
        return null;
    }

    private static ulong OwnerKey(string ownerId) => XxHash64.HashToUInt64(Encoding.UTF8.GetBytes(ownerId));

    private static void Append(XxHash64 h, Span<byte> b, long v)
    {
        BitConverter.TryWriteBytes(b, v);
        h.Append(b);
    }

    private static void AppendString(XxHash64 h, Span<byte> b, string s)
    {
        Append(h, b, s.Length);
        h.Append(Encoding.UTF8.GetBytes(s));
    }
}
