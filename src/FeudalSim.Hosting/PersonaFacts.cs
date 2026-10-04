using System.Text;
using FeudalSim.AI.Dialogue;
using FeudalSim.Sim;
using FeudalSim.Sim.Core;
using FeudalSim.Sim.Dialogue;
using FeudalSim.Sim.Social;
using FeudalSim.Sim.Time;

namespace FeudalSim.Hosting;

/// <summary>
/// 22 §7.3–7.4: the persona card and the per-turn facts, rendered deterministically from sim state through band tables —
/// a model never sees a raw number it could be asked to change. Sim thread only.
/// </summary>
public static class PersonaFacts
{
    public static PromptFacts For(SimWorld world, int npc, int player, Conversation conv)
    {
        var people = world.People;
        if (npc < 0) { return new PromptFacts("Name: someone.", "", "", [.. conv.Transcript], [], []); }
        EntityId me = people.Ids[npc], them = player >= 0 ? people.Ids[player] : EntityId.None;
        var name = people.Names[npc];
        var playerName = player >= 0 ? people.Names[player] : "the stranger";
        ref readonly var p = ref people.Personality[npc];

        var persona = new StringBuilder();
        var age = (int)((world.Clock.GameMinute - people.Core[npc].BirthGameMinute) / GameDate.MinutesPerYear);
        var profession = p.Profession < world.Content.Professions.Count ? world.Content.Professions[p.Profession].Name : "settler";
        var culture = p.Culture < world.Content.Cultures.Count ? world.Content.Cultures[p.Culture] : null;
        persona.Append("Name: ").Append(name).Append(", ").Append(age).Append(". ").Append(profession).AppendLine(", one of the settlers who came ashore at Landfall.");
        if (culture is not null) { persona.Append("From: ").Append(culture.Name).Append(culture.Faith is { } f ? $"; {f.Replace('_', ' ')}." : ".").AppendLine(); }
        persona.Append("Temperament: ").Append(string.Join("; ", Temperament(world, npc))).AppendLine(".");
        var values = Values(p.Values);
        persona.Append("Cares most about: ").Append(string.Join(", ", values.Take(3))).Append(". Cares little for: ").Append(string.Join(", ", values.TakeLast(2))).AppendLine(".");
        persona.Append("Voice: ").Append(Voice(culture?.Id)).Append(p.Volatility >= 65 ? " Quick, sharp sentences." : p.Warmth >= 65 ? " Warm and easy." : " Plain and short.");

        // NOW + feelings + toward the speaker (22 §7.5).
        var date = GameDate.FromGameMs(world.Clock.GameMs);
        var now = new StringBuilder().Append(TimeOfDay(world.Clock.GameMinute % 1440)).Append(", ").Append(date.Season).Append(", at the Landfall camp");
        var others = Enumerable.Range(0, people.Count).Where(k => k != npc && k != player && Escalation.Within(world, npc, k, 8f) && !people.Activity[k].Has(Sim.World.ActivityState.Asleep))
            .Select(k => people.Names[k]).Take(4).ToList();
        if (others.Count > 0) { now.Append(". Also present: ").Append(string.Join(", ", others)); }
        now.Append('.').AppendLine();
        var feels = Feelings(world, npc, them, playerName);
        now.Append(name.Split(' ')[0].ToUpperInvariant()).Append(" FEELS: ").Append(feels.Count == 0 ? "calm" : string.Join(". ", feels)).Append('.');

        var rel = world.Relationships;
        var toward = $"TOWARD {playerName.Split(' ')[0].ToUpperInvariant()}: {OpinionWords(rel.Opinion(me, them))}; {TrustWords(rel.Trust(me, them))}; {FamiliarityWords(rel.Familiarity(me, them))}"
                     + (rel.Fear(me, them) >= 50f ? "; is afraid of them" : "") + ".";

        var known = Enumerable.Range(0, people.Count).Select(k => people.Names[k]).ToList();
        var temperature = Math.Clamp(0.7f + (0.1f * (p.Volatility - 50f) / 15f), 0.6f, 0.85f);   // 22 §4.8 persona range
        return new PromptFacts(persona.ToString(), now.ToString(), toward, [.. conv.Transcript], Knows(world, npc, them, playerName), known, Temperature: temperature);
    }

    /// <summary>YOU KNOW: what this person remembers of the speaker and the strongest things they hold true (k ≤ 6).</summary>
    private static List<string> Knows(SimWorld world, int npc, EntityId them, string playerName)
    {
        var people = world.People;
        var me = people.Ids[npc];
        var now = world.Clock.GameMinute;
        var lines = new List<string>();
        foreach (var m in world.Memories.Span(me).ToArray().Where(m => m.Actor == them.Value).OrderByDescending(m => m.S0).Take(3))
        {
            var ago = ((now - m.TimeMin) / 1440) switch { 0 => "today", 1 => "yesterday", var d => $"{d} days ago" };
            lines.Add($"{playerName} {MemoryWords(m.Kind)} ({ago}).");
        }

        foreach (var b in world.Beliefs.Span(me).ToArray().Where(b => b.C >= BeliefStore.Hold).OrderByDescending(b => b.C * world.Content.ClaimPredicates[world.Claims[b.Claim].Predicate].Juiciness).Take(3))
        {
            lines.Add((b.FirstHand ? "(saw it) " : "(heard) ") + ClaimText(world, b.Claim) + ".");
        }

        return lines;
    }

    public static string ClaimText(SimWorld world, int claim)
    {
        ref readonly var c = ref world.Claims[claim];
        var phrase = world.Content.ClaimPredicates[c.Predicate].Phrase;
        string Name(ulong id) => world.People.IndexOf(new EntityId(id)) is var r and >= 0 ? world.People.Names[r] : "someone";
        return phrase.Replace("{subject}", Name(c.Subject), StringComparison.Ordinal).Replace("{object}", Name(c.Object), StringComparison.Ordinal);
    }

    private static List<string> Temperament(SimWorld world, int npc)
    {
        var p = world.People.Personality[npc];
        var parts = new List<string>();
        for (var bits = p.Traits; bits != 0; bits &= bits - 1)
        {
            var h = System.Numerics.BitOperations.TrailingZeroCount(bits);
            if (h < world.Content.Traits.Count) { parts.Add(world.Content.Traits[h].Name.ToLowerInvariant()); }
        }

        float Z(byte f) => (f - 50f) / 15f;
        if (Z(p.Warmth) >= 1) { parts.Add("warm-hearted"); } else if (Z(p.Warmth) <= -1) { parts.Add("gruff with people"); }
        if (Z(p.Volatility) >= 1) { parts.Add("quick to flare up"); } else if (Z(p.Volatility) <= -1) { parts.Add("hard to rattle"); }
        if (Z(p.Sociability) >= 1) { parts.Add("talkative"); } else if (Z(p.Sociability) <= -1) { parts.Add("keeps to themselves"); }
        if (Z(p.Diligence) >= 1) { parts.Add("hardworking"); } else if (Z(p.Diligence) <= -1) { parts.Add("easygoing about work"); }
        if (Z(p.Curiosity) >= 1) { parts.Add("curious"); }
        return parts.Count == 0 ? ["even-keeled"] : parts;
    }

    private static List<string> Values(Sim.World.ValueBlock v)
        => [.. new (string, byte)[] { ("family", v.Family), ("wealth", v.Wealth), ("standing", v.Status), ("honor", v.Honor), ("tradition", v.Tradition),
                ("faith", v.Faith), ("fairness", v.Fairness), ("freedom", v.Freedom), ("loyalty", v.Loyalty) }.OrderByDescending(x => x.Item2).Select(x => x.Item1)];

    private static string Voice(string? culture) => culture switch
    {
        "culture.varrow" => "Formal address, \"aye\" and \"nay\".",
        "culture.osmeri" => "Coin and trade idioms.",
        "culture.brannoch" => "Kin and oath talk.",
        "culture.ashen_reform" => "Plain, scripture-flavored speech.",
        _ => "Plain speech.",
    };

    private static List<string> Feelings(SimWorld world, int npc, EntityId them, string playerName)
    {
        var people = world.People;
        var e = people.Emotions[npc];
        var n = people.Needs[npc];
        var f = new List<string>();
        void E(float v, string a, string b, string c, string d, EntityId? target)
        {
            var word = v switch { >= 80 => d, >= 60 => c, >= 40 => b, >= 20 => "a little " + a, _ => null };
            if (word is not null) { f.Add(target is { } t && t == them && !t.IsNone ? $"{word}, at {playerName}" : word); }
        }

        E(e.Anger, "irritated", "annoyed", "angry", "furious", e.AngerTarget);
        E(e.Fear, "uneasy", "afraid", "frightened", "terrified", null);
        E(e.Grief, "sad", "grieving", "grief-stricken", "devastated", null);
        E(e.Joy, "pleased", "happy", "delighted", "overjoyed", null);
        E(e.Shame, "embarrassed", "ashamed", "deeply ashamed", "mortified", null);
        if (n.Satiety < 15) { f.Add("starving"); } else if (n.Satiety < 30) { f.Add("hungry"); }
        if (n.Hydration < 15) { f.Add("parched"); } else if (n.Hydration < 30) { f.Add("thirsty"); }
        if (n.Energy < 15) { f.Add("exhausted"); } else if (n.Energy < 30) { f.Add("tired"); }
        if (n.Social < 25) { f.Add("lonely"); }
        var mood = people.Mood[npc].Smoothed;
        if (mood <= -60) { f.Add("at the end of their rope"); } else if (mood <= -20) { f.Add("in low spirits"); } else if (mood >= 60) { f.Add("in high spirits"); }
        return f;
    }

    public static string OpinionWords(float op) => op switch
    {
        <= -60 => "hates them", <= -20 => "dislikes them", <= -6 => "dislikes them a little", <= 5 => "has no strong feeling about them",
        <= 19 => "likes them a little", <= 59 => "likes them", _ => "is very fond of them",
    };

    public static string TrustWords(float t) => t switch { < 25 => "distrusts them", < 50 => "is wary of them", < 75 => "trusts them", _ => "trusts them completely" };

    public static string FamiliarityWords(float f) => f switch { < 10 => "a stranger", < 40 => "knows them slightly", < 70 => "knows them well", _ => "knows them intimately" };

    private static string TimeOfDay(long minute) => minute switch
    {
        < 300 => "Night", < 420 => "Dawn", < 660 => "Morning", < 780 => "Midday", < 1020 => "Afternoon", < 1200 => "Evening", _ => "Night",
    };

    private static string MemoryWords(MemoryKind k) => k switch
    {
        MemoryKind.Chat => "chatted with them", MemoryKind.Joke => "joked with them", MemoryKind.Praise => "praised them",
        MemoryKind.Comfort => "comforted them", MemoryKind.Help => "helped them", MemoryKind.Refused => "refused them a favor",
        MemoryKind.Argue => "argued with them", MemoryKind.Insult => "insulted them", MemoryKind.Apology => "apologized to them",
        MemoryKind.Gift => "gave them a gift", MemoryKind.Threat => "threatened them", MemoryKind.Assault => "fought them",
        MemoryKind.Told => "told them some news", _ => "warned them",
    };
}
