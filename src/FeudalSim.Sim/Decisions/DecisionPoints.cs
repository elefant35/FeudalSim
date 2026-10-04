using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using MessagePack;

namespace FeudalSim.Sim.Decisions;

// Decision points (canon §13.1, 20 §11, 22 §6): an owning system builds a menu; a decider (LLM, fast decider or
// the deterministic policy) picks one option; the DRE guards the pick; the owner executes it. Names follow 20
// (commands and logging); the option fields follow 22 §6. 22's DecisionSubmitted is DecisionMade here.

public enum Stakes : byte { Low, Medium, High, Critical }

/// <summary>Who may decide (on a DP) or who did decide (on a decision). Policy-only DPs resolve inline.</summary>
public enum DeciderKind : byte { Policy, Llm, Fast }

/// <summary>Why a decision resolved the way it did (22 §6). Anything but <see cref="Passed"/> means the policy chose.</summary>
public enum GuardOutcome : byte
{
    Passed, OffMenu, Ineligible, BelowFloor, LongShotBudget, CriticalCheck, StaleMenu, Deadline, ModePolicy, Inline,
}

/// <summary>A fixed parameter of an option, set by the owning system (never by a decider): price_f, qty, target…</summary>
[MessagePackObject]
public readonly record struct OptionParam([property: Key(0)] string Key, [property: Key(1)] long Value);

/// <summary>One menu entry (22 §6). <c>P</c> is the base propensity p_i over eligible options; <c>PCrit</c> is p_i
/// recomputed from sim state alone (critical options only).</summary>
[MessagePackObject]
public sealed record MenuOption(
    [property: Key(0)] string Id,
    [property: Key(1)] string Family,
    [property: Key(2)] OptionParam[] Params,
    [property: Key(3)] bool Eligible,
    [property: Key(4)] float P,
    [property: Key(5)] float PCrit,
    [property: Key(6)] Stakes Stakes,
    [property: Key(7)] bool FavorsPlayer,
    [property: Key(8)] string Gloss);

/// <summary>What an owner needs to (re)build a menu: the same context and state always give the same menu.</summary>
[MessagePackObject]
public readonly record struct DpContext(
    [property: Key(0)] string Kind,
    [property: Key(1)] EntityId Chooser,
    [property: Key(2)] EntityId Counterpart,
    [property: Key(3)] long Subject);

/// <summary>Implemented by the systems that own decisions (15 trade, 16 social, 17 politics, 18 conflict, 12, 21).</summary>
public interface IDecisionPointOwner
{
    string OwnerId { get; }

    /// <summary>Pure: same state and context → same menu. Called when the DP opens and again at commit (22 §6.6).</summary>
    IReadOnlyList<MenuOption> BuildMenu(SimWorld world, in DpContext context);

    /// <summary>Enacts the chosen option and resolves every consequence deterministically.</summary>
    void Execute(SimWorld world, DecisionPoint dp, MenuOption chosen);
}

/// <summary>An open decision point as the DRE holds it.</summary>
public sealed class DecisionPoint
{
    public required ulong Id { get; init; }
    public required string Owner { get; init; }
    public required DpContext Context { get; init; }

    /// <summary>Canonical order (option id, ordinal); the menu hash is computed over this.</summary>
    public required MenuOption[] Menu { get; init; }

    public required ulong MenuHash { get; init; }

    /// <summary>Options that pass the guards at open — the only ones a decider is shown (22 §6.6).</summary>
    public required string[] PreCleared { get; init; }

    public required DeciderKind MaxDecider { get; init; }
    public required long OpenStep { get; init; }
    public required long DeadlineStep { get; init; }

    /// <summary>The policy's pick, drawn when the DP opened, so the fallback never depends on whether a model answered.</summary>
    public required string PolicyChoice { get; init; }
}

/// <summary>
/// Integrity record (20 §8.5): written to the input log when the sim opens a DP a model may decide, and the
/// gateway's work order. Verified, not applied — the sim re-opens the DP itself; this checks it matches.
/// </summary>
[MessagePackObject]
public sealed record DecisionPointOpened(
    [property: Key(0)] ulong Id,
    [property: Key(1)] string Owner,
    [property: Key(2)] DpContext Context,
    [property: Key(3)] ulong MenuHash,
    [property: Key(4)] MenuOption[] Options,
    [property: Key(5)] string[] PreCleared,
    [property: Key(6)] long OpenStep,
    [property: Key(7)] long DeadlineStep,
    [property: Key(8)] DeciderKind MaxDecider) : StateCommand;

/// <summary>
/// A decider's answer (20 §11; 22's DecisionSubmitted). <c>Choice = null</c> means "policy, now" (template mode,
/// budget out, injection flag, chain exhausted). <c>Probabilities</c> is telemetry only — no rule reads it.
/// </summary>
[MessagePackObject]
public sealed record DecisionMade(
    [property: Key(0)] ulong Id,
    [property: Key(1)] ulong MenuHash,
    [property: Key(2)] string? Choice,
    [property: Key(3)] DeciderKind Decider,
    [property: Key(4)] string ProviderTag,
    [property: Key(5)] int LatencyMs,
    [property: Key(6)] float[]? Probabilities) : StateCommand;
