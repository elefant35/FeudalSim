using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FeudalSim.Sim.Content;

namespace FeudalSim.Content;

/// <summary>
/// Generates JSON Schemas (draft 2020-12) from the C# definition records: snake_case property names,
/// snake_case enum values, required = C# `required` members, no additional properties. Deterministic
/// output, so committed schemas can be checked for freshness in CI.
/// </summary>
public static class SchemaGenerator
{
    /// <summary>Content kinds: folder name, id prefix, definition type.</summary>
    public static readonly IReadOnlyList<(string Folder, string Kind, Type Type)> Kinds =
    [
        ("actions", "action", typeof(ActionDef)),
        ("assets", "asset", typeof(AssetDef)),
        ("audio", "audio", typeof(AudioEventDef)),
        ("social/claim_predicates.yaml", "claim", typeof(ClaimPredicateDef)),
        ("cultures", "culture", typeof(CultureDef)),
        ("flaws", "flaw", typeof(FlawDef)),
        ("decisions", "dp", typeof(DecisionDef)),
        ("diseases", "disease", typeof(DiseaseDef)),
        ("items", "item", typeof(ItemDef)),
        ("lines", "line", typeof(LineTemplateDef)),
        ("minigames", "minigame", typeof(MinigameDef)),
        ("needs", "need", typeof(NeedDef)),
        ("nodes", "node", typeof(NodeDef)),
        ("professions", "profession", typeof(ProfessionDef)),
        ("recipes", "recipe", typeof(RecipeDef)),
        ("schedules", "schedule", typeof(ScheduleDef)),
        ("social/opinion_modifiers.yaml", "opinion", typeof(OpinionModifierDef)),
        ("social/overheard_lines.yaml", "overheard", typeof(OverheardLineDef)),
        ("skills", "skill", typeof(SkillDef)),
        ("traits", "trait", typeof(TraitDef)),
        ("world", "worldspec", typeof(WorldSpecDef)),
    ];

    public static string Generate(string kind, Type type)
    {
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = $"https://feudalsim.dev/schemas/{kind}.schema.json",
            ["title"] = type.Name,
            ["description"] = $"A list of {kind} definitions (generated from {type.FullName}; do not edit).",
            ["type"] = "array",
            ["items"] = ObjectSchema(type, $"^{kind}(\\.[a-z0-9_]+)+$"),
        };
        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true, TypeInfoResolver = new DefaultJsonTypeInfoResolver() }) + "\n";
    }

    private static JsonObject ObjectSchema(Type type, string? idPattern = null)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var name = JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name);
            // Required exactly when the C# member is declared `required`; everything else has a default.
            var required_ = p.IsDefined(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute), inherit: false);
            var s = TypeSchema(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType);
            if (name == "id" && idPattern is not null) { s["pattern"] = idPattern; }
            properties[name] = s;
            if (required_) { required.Add(name); }
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
        };
    }

    private static JsonObject TypeSchema(Type t)
    {
        if (t == typeof(string)) { return new JsonObject { ["type"] = "string", ["minLength"] = 1 }; }
        if (t == typeof(bool)) { return new JsonObject { ["type"] = "boolean" }; }
        if (t == typeof(int) || t == typeof(long)) { return new JsonObject { ["type"] = "integer" }; }
        if (t == typeof(float) || t == typeof(double)) { return new JsonObject { ["type"] = "number" }; }
        if (t.IsEnum)
        {
            var values = new JsonArray();
            foreach (var n in Enum.GetNames(t)) { values.Add(JsonNamingPolicy.SnakeCaseLower.ConvertName(n)); }
            return new JsonObject { ["type"] = "string", ["enum"] = values };
        }

        if (t == typeof(object)) { return new JsonObject(); }
        if (t.IsGenericType && t.GetGenericTypeDefinition() is var g && (g == typeof(IReadOnlyDictionary<,>) || g == typeof(IDictionary<,>) || g == typeof(Dictionary<,>)))
        {
            return new JsonObject { ["type"] = "object" };
        }

        if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t) && t.IsGenericType)
        {
            return new JsonObject { ["type"] = "array", ["items"] = TypeSchema(t.GetGenericArguments()[0]) };
        }

        return ObjectSchema(t);
    }
}
