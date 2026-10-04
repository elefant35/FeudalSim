using System.Globalization;
using System.Text;
using FeudalSim.Sim.Ai;
using FeudalSim.Sim.Core;

namespace FeudalSim.Sim.Social;

/// <summary>
/// Overheard NPC↔NPC talk (22 §9.2): the policy has already decided and resolved the exchange; this builds the render
/// request: a facts payload (persona lite for both, what happened, the claim voiced and the listener's choice, the names a
/// line may use) and the template subtitle that plays if no model answers in time. Rendering never changes the outcome.
/// </summary>
public static class Overheard
{
    public static long Request(SimWorld world, int a, int b, string kind, bool success, int claim, ToldOption option)
    {
        var people = world.People;
        var names = new List<string> { people.Names[a], people.Names[b] };
        var claimText = claim >= 0 ? ClaimText(world, claim, names) : null;

        var json = new StringBuilder(640).Append('{');
        Field(json, "setting", Setting).Append(',');
        Field(json, "place", Place(world, a)).Append(',');
        Field(json, "time", TimeOfDay(world)).Append(',');
        Field(json, "kind", kind).Append(',');
        json.Append("\"success\":").Append(success ? "true" : "false").Append(',');
        json.Append("\"a\":"); Persona(json, world, a); json.Append(',');
        json.Append("\"b\":"); Persona(json, world, b);
        if (claimText is not null)
        {
            json.Append(',');
            Field(json, "claim", claimText).Append(',');
            Field(json, "claim_about", NameOf(world, world.Claims[claim].Subject, null)).Append(',');
            Field(json, "listener_choice", option switch
            {
                ToldOption.Doubt => "doubts it",
                ToldOption.Believe => "believes it",
                ToldOption.Repeat => "believes it and will pass it on",
                _ => "believes it but will keep it quiet",
            });
        }

        json.Append(",\"names\":[");
        for (var k = 0; k < names.Count; k++) { if (k > 0) { json.Append(','); } Quote(json, names[k]); }
        json.Append("]}");

        return world.RequestAi(AiTaskKind.Overheard, AiPriority.Proximate, Systems.InteractionSystem.OverheardDeadlineSteps,
            people.Ids[a], people.Ids[b], json.ToString(), Subtitle(world, kind, success, people.Names[a], people.Names[b], claimText));
    }

    /// <summary>The M1 graybox world: one landfall camp. Grounds small talk so a model has nothing to invent.</summary>
    public const string Setting = "a landfall camp: settlers newly arrived by ship, living around one fire by the shore; no village, houses, animals or other people yet";

    private static string Place(SimWorld world, int row)
    {
        var action = world.People.Activity[row].Action;
        if (action < 0) { return "walking through the camp"; }
        return world.Content.Actions[action].Place switch
        {
            Content.PlaceKind.Fire => "at the fire",
            Content.PlaceKind.Water => "at the stream",
            Content.PlaceKind.Stores => "by the food stores",
            Content.PlaceKind.Shelter => "by the shelters",
            Content.PlaceKind.Woods => "gathering wood at the forest edge",
            Content.PlaceKind.ForageGround => "foraging",
            _ => "in the camp",
        };
    }

    private static string TimeOfDay(SimWorld world)
    {
        var hour = world.Clock.GameMinute % 1440 / 60;
        return hour switch { < 5 => "night", < 12 => "morning", < 17 => "afternoon", < 21 => "evening", _ => "night" };
    }

    /// <summary>The first template line (id order) for this kind and outcome, with names and the claim filled in.</summary>
    public static string Subtitle(SimWorld world, string kind, bool success, string a, string b, string? claim)
    {
        foreach (var line in world.Content.OverheardLines)
        {
            if (line.Interaction != kind || (line.Success is { } s && s != success)) { continue; }
            return line.Text.Replace("{a}", a, StringComparison.Ordinal).Replace("{b}", b, StringComparison.Ordinal)
                .Replace("{claim}", claim ?? "something", StringComparison.Ordinal);
        }

        return $"{a} and {b} talk quietly.";
    }

    /// <summary>The claim's phrase with names ("Hob stole from Tam"); adds the names it uses to <paramref name="names"/>.</summary>
    public static string ClaimText(SimWorld world, int claimId, List<string>? names = null)
    {
        ref readonly var c = ref world.Claims[claimId];
        var def = world.Content.ClaimPredicates[c.Predicate];
        var subject = NameOf(world, c.Subject, names);
        var obj = c.Object == 0 ? "someone" : NameOf(world, c.Object, names);
        var text = def.Phrase.Replace("{subject}", subject, StringComparison.Ordinal).Replace("{object}", obj, StringComparison.Ordinal);
        if ((c.Qualifiers & ClaimQualifiers.Hedged) != 0) { text = "maybe " + text; }
        return text;
    }

    private static string NameOf(SimWorld world, ulong id, List<string>? names)
    {
        var row = world.People.IndexOf(new EntityId(id));
        if (row < 0) { return "someone"; }
        var name = world.People.Names[row];
        if (names is not null && !names.Contains(name)) { names.Add(name); }
        return name;
    }

    /// <summary>Persona lite (22 §9.2, ≈ 80 tokens): name, trade, temperament words from facets, a strong feeling if any.</summary>
    private static void Persona(StringBuilder json, SimWorld world, int row)
    {
        var people = world.People;
        ref readonly var p = ref people.Personality[row];
        ref readonly var e = ref people.Emotions[row];
        var words = new List<string>(4);
        if (p.Warmth >= 65) { words.Add("warm"); } else if (p.Warmth <= 35) { words.Add("cool"); }
        if (p.Sociability >= 65) { words.Add("talkative"); } else if (p.Sociability <= 35) { words.Add("reserved"); }
        if (p.Volatility >= 65) { words.Add("quick to anger"); } else if (p.Volatility <= 35) { words.Add("even-tempered"); }
        if (p.Curiosity >= 65) { words.Add("curious"); }
        if (words.Count == 0) { words.Add("plain-spoken"); }
        var feeling = e.Anger >= 40f ? "angry" : e.Grief >= 40f ? "grieving" : e.Fear >= 40f ? "afraid" : e.Shame >= 40f ? "ashamed" : e.Joy >= 40f ? "cheerful" : null;

        json.Append('{');
        Field(json, "name", people.Names[row]).Append(',');
        var profession = p.Profession < world.Content.Professions.Count ? world.Content.Professions[p.Profession].Name : "settler";
        Field(json, "trade", profession).Append(',');
        Field(json, "temperament", string.Join(", ", words));
        if (feeling is not null) { json.Append(','); Field(json, "feeling", feeling); }
        json.Append('}');
    }

    private static StringBuilder Field(StringBuilder json, string key, string value)
    {
        Quote(json, key).Append(':');
        return Quote(json, value);
    }

    private static StringBuilder Quote(StringBuilder json, string s)
    {
        json.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case < ' ': json.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture)); break;
                default: json.Append(ch); break;
            }
        }

        return json.Append('"');
    }
}
