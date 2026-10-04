using FeudalSim.AI.Dialogue;
using FeudalSim.Sim;
using FeudalSim.Sim.Commands;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Decisions;
using FeudalSim.Sim.Dialogue;

namespace FeudalSim.Hosting;

/// <summary>
/// A classified player line not yet said (19 §6.3): the intent echo, and whether it is consequential — then the UI
/// confirms it (auto after 1.5 s) or the player unsays it, and nothing reaches the sim.
/// </summary>
public sealed record PendingTurn(ulong Conversation, string Text, Classification Classification, PlayerUtteranceClassified Command, bool Consequential, bool Downgraded, string Echo);

/// <summary>
/// What the player may see of an NPC's choice after it is made (19 §6.6, §6.9): the stance it implies, a glyph, a
/// proposal's fixed terms. Never the menu, the options not taken, or their propensities.
/// </summary>
public sealed record TurnOutcome(ulong Conversation, string Owner, string Chosen, string? Stance, string? Glyph, string? Proposal);

/// <summary>Quick intents (19 §6.2): structured acts that skip the classifier, with the neutral words signal.</summary>
public sealed record QuickIntent(string Act, string Shown, string RequestTask = "", float RequestHours = 0f, int Severity = 0);

public static class DialogueTurns
{
    /// <summary>19 §6.3: acts that create obligations, crimes or fights wait for confirmation (or an unsay).</summary>
    public static bool IsConsequential(string act) => act is "threaten" or "insult" or "promise" or "trade_offer" or "accept_offer";

    /// <summary>19 §6.3: under 0.55 confidence a consequential act becomes its nearest non-consequential act.</summary>
    public static string Downgrade(string act) => act switch
    {
        "threaten" => "tell", "insult" => "small_talk", "promise" => "small_talk", "trade_offer" => "ask", "accept_offer" => "small_talk", _ => act,
    };

    public static string ActName(string act) => act switch
    {
        "small_talk" => "Small talk", "greet_farewell" => "Greeting", "thank" => "Thanks", "praise" => "Compliment", "comfort" => "Comfort",
        "ask" => "Question", "why_did_you" => "Asking why", "nonsense_or_meta" => "Nonsense", "apologize" => "Apology", "flirt" => "Flirtation",
        "tell" => "Telling", "persuade" => "Persuasion", "request" => "Request", "promise" => "Promise", "trade_offer" => "Trade offer",
        "reject_offer" => "Declining", "accept_offer" => "Accepting", "command" => "Order", "insult" => "Insult", "threaten" => "Threat", _ => act,
    };

    /// <summary>The intent echo: "read as: Request · polite", or "read as: Warning (unclear)" when downgraded.</summary>
    public static string Echo(string act, string tone, bool downgraded, string? original)
        => downgraded ? $"read as: {(act == "tell" && original == "threaten" ? "Warning" : ActName(act))} (unclear)" : $"read as: {ActName(act)}{(tone is "" or "neutral" ? "" : " · " + tone.Replace('_', ' '))}";

    /// <summary>19 §6.5's stance on the current topic, restating a choice already shown.</summary>
    public static string? Stance(string chosen) => chosen switch
    {
        "accept_request" or "accept_with_condition" or "accept_apology" or "believe" or "repeat" or "keep_quiet" or "accept_offer" or "buy_at_ask" => "agreeable",
        "defer" or "demand_amends" => "bargaining",
        _ when chosen.StartsWith("counter_step_", StringComparison.Ordinal) => "bargaining",
        "refuse_request" or "refuse_apology" or "doubt" or "refuse" => "unmoved",
        "retort" or "threaten" or "shove" or "attack_brawl" or "attack_armed" or "attack_to_kill" => "bristling",
        "warm_to_speaker" => "warming",
        "cool_to_speaker" => "cooling",
        _ => null,
    };

    /// <summary>The 19 §6.6 glyph a choice earns; ✧ needs the presented menu (favoring and not the policy's most likely).</summary>
    public static string? Glyph(DecisionPointOpened? dp, string chosen)
    {
        if (chosen == "warm_to_speaker") { return "▲ warmer"; }
        if (chosen == "cool_to_speaker") { return "▼ cooler"; }
        var talkedRound = dp is not null && dp.Options.FirstOrDefault(o => o.Id == chosen) is { FavorsPlayer: true } c
            && dp.Options.Where(o => dp.PreCleared.Contains(o.Id)).Max(o => o.P) > c.P;
        if (talkedRound) { return "✧ talked round"; }
        return chosen is "accept_request" or "accept_with_condition" or "defer" ? "✦ promise noted" : null;
    }

    /// <summary>
    /// 19 §6.5 demeanor cue, filtered by how well the player reads this person: one roll per conversation,
    /// p_read = clamp(0.20 + 0.006·Familiarity + 0.003·Persuasion, 0.05, 0.95) (Perception and concealment join with
    /// 21's deceive goal); a stranger shows one cue at most.
    /// </summary>
    public static string Cue(SimWorld world, Conversation conv)
    {
        var people = world.People;
        var npc = people.IndexOf(conv.Npc);
        if (npc < 0 || world.PlayerRow < 0) { return ""; }
        var familiarity = world.Relationships.Familiarity(conv.Player, conv.Npc);
        var pRead = Math.Clamp(0.20f + (0.006f * familiarity) + (0.003f * Sim.Decisions.MenuWidth.Persuasion(world, world.PlayerRow)), 0.05f, 0.95f);
        var roll = (SplitMix64.Avalanche(conv.Id ^ 0xC0E5_1D) >> 11) * (1.0 / (1UL << 53));   // presentation only, not sim state
        if (roll >= pRead) { return "hard to read"; }
        var cues = new List<string>();
        var op = world.Relationships.Opinion(conv.Npc, conv.Player);
        cues.Add(op <= -50 ? "hostile" : op <= -15 ? "cold" : op >= 50 ? "fond" : op >= 15 ? "warm" : "neutral");
        var e = people.Emotions[npc];
        var (emotion, level) = new[] { ("angry", e.Anger), ("afraid", e.Fear), ("grieving", e.Grief), ("cheerful", e.Joy), ("ashamed", e.Shame) }.MaxBy(x => x.Item2);
        if (level > 40) { cues.Add(emotion); }
        var n = people.Needs[npc];
        if (n.Energy < 30) { cues.Add("tired"); } else if (n.Satiety < 30) { cues.Add("hungry"); }
        if (world.Relationships.Fear(conv.Npc, conv.Player) > 50) { cues.Add("wary"); }
        return string.Join(", ", familiarity < 20 ? cues.Take(1) : cues);
    }
}
