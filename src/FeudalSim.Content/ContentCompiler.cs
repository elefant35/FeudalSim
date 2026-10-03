using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using FeudalSim.Sim.Content;
using Json.Schema;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace FeudalSim.Content;

/// <summary>
/// The content pipeline (20 §10): parse YAML → JSON Schema per kind → semantic validation → compile
/// to a <see cref="ContentDatabase"/> with sorted handles and a <see cref="ContentDatabase.Hash"/>.
/// </summary>
public static class ContentCompiler
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    // JsonSchema.Net registers schemas globally by $id, so each kind's schema is compiled exactly once.
    private static readonly IReadOnlyDictionary<string, JsonSchema> Schemas =
        SchemaGenerator.Kinds.ToDictionary(k => k.Kind, k => JsonSchema.FromText(SchemaGenerator.Generate(k.Kind, k.Type)));

    public sealed record Result(ContentDatabase? Database, IReadOnlyList<ContentError> Errors, int FileCount)
    {
        public bool Ok => Database is not null && Errors.Count == 0;
    }

    public static Result Compile(string contentRoot)
    {
        var errors = new List<ContentError>();
        var skills = new List<SkillDef>();
        var items = new List<ItemDef>();
        var needs = new List<NeedDef>();
        var assets = new List<AssetDef>();
        var audio = new List<AudioEventDef>();
        var repoRoot = Path.GetDirectoryName(Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar)) ?? contentRoot;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = 0;

        foreach (var (folder, kind, type) in SchemaGenerator.Kinds)
        {
            var schema = Schemas[kind];
            var dir = Path.Combine(contentRoot, folder);
            if (!Directory.Exists(dir)) { continue; }
            foreach (var file in Directory.GetFiles(dir, "*.yaml").OrderBy(f => f, StringComparer.Ordinal))
            {
                files++;
                var rel = Path.GetRelativePath(contentRoot, file).Replace('\\', '/');
                foreach (var (node, mark) in ParseFile(file, rel, kind, schema, errors))
                {
                    object? def;
                    try { def = node.Deserialize(type, Json); }
                    catch (JsonException ex) { errors.Add(new(rel, mark.Line, mark.Column, ex.Message)); continue; }

                    var id = (string)type.GetProperty("Id")!.GetValue(def)!;
                    if (!id.StartsWith(kind + ".", StringComparison.Ordinal))
                    {
                        errors.Add(new(rel, mark.Line, mark.Column, $"Id '{id}' must start with '{kind}.' (files in {folder}/ hold only {kind} definitions)."));
                        continue;
                    }

                    if (!seen.TryAdd(id, $"{rel}:{mark.Line}"))
                    {
                        errors.Add(new(rel, mark.Line, mark.Column, $"Duplicate id '{id}' (first defined at {seen[id]})."));
                        continue;
                    }

                    switch (def)
                    {
                        case SkillDef s: skills.Add(s); break;
                        case ItemDef i: ValidateItem(i, rel, mark, errors); items.Add(i); break;
                        case NeedDef n: ValidateNeed(n, rel, mark, errors); needs.Add(n); break;
                        case AssetDef a: ValidateAsset(a, rel, mark, repoRoot, errors); assets.Add(a); break;
                        case AudioEventDef e: ValidateAudio(e, rel, mark, repoRoot, errors); audio.Add(e); break;
                    }
                }
            }
        }

        CheckCanonical("skills", CanonLists.SkillIds, skills.Select(s => s.Id), errors);
        CheckCanonical("needs", CanonLists.NeedIds, needs.Select(n => n.Id), errors);
        if (errors.Count > 0) { return new Result(null, errors, files); }

        skills.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        items.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        needs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        assets.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        audio.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return new Result(new ContentDatabase(skills, items, needs, Hash(skills, items, needs), assets, audio), errors, files);
    }

    private static IEnumerable<(JsonNode Node, Mark Mark)> ParseFile(string path, string rel, string kind, JsonSchema schema, List<ContentError> errors)
    {
        var stream = new YamlStream();
        try
        {
            using var reader = new StreamReader(path);
            stream.Load(reader);
        }
        catch (YamlException ex)
        {
            errors.Add(new(rel, (int)ex.Start.Line, (int)ex.Start.Column, $"YAML syntax: {ex.Message}"));
            yield break;
        }

        if (stream.Documents.Count == 0) { yield break; }
        var marks = new Dictionary<string, Mark>(StringComparer.Ordinal);
        var root = YamlJson.Convert(stream.Documents[0].RootNode, "", marks);
        var result = schema.Evaluate(JsonSerializer.SerializeToElement(root), new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!result.IsValid)
        {
            foreach (var detail in (result.Details ?? []).Where(d => d.Errors is { Count: > 0 }))
            {
                var pointer = detail.InstanceLocation.ToString();
                var mark = marks.TryGetValue(pointer, out var m) ? m : stream.Documents[0].RootNode.Start;
                foreach (var (keyword, message) in detail.Errors!)
                {
                    errors.Add(new(rel, (int)mark.Line, (int)mark.Column, $"{(pointer.Length == 0 ? "/" : pointer)}: {message} ({keyword})"));
                }
            }

            yield break;
        }

        if (root is not JsonArray list) { yield break; }
        for (var i = 0; i < list.Count; i++)
        {
            yield return (list[i]!, marks[$"/{i}"]);
        }
    }

    private static void ValidateItem(ItemDef i, string rel, Mark m, List<ContentError> errors)
    {
        if (i.MassKg <= 0) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: mass_kg must be > 0.")); }
        if (i.BaseValueF < 0) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: base_value_f must be ≥ 0 farthings.")); }
        if (i.Durability is <= 0) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: durability must be > 0 when set.")); }
    }

    private static void ValidateAsset(AssetDef a, string rel, Mark m, string repoRoot, List<ContentError> errors)
    {
        foreach (var output in a.Outputs)
        {
            if (!File.Exists(Path.Combine(repoRoot, output))) { errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: output '{output}' does not exist.")); }
        }

        if (a.Source.Type == AssetSourceType.Generator && (a.Source.Generator is null || !File.Exists(Path.Combine(repoRoot, a.Source.Generator))))
        {
            errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: generator source '{a.Source.Generator}' does not exist."));
        }

        if (a.License.AttributionRequired && string.IsNullOrWhiteSpace(a.License.Credit))
        {
            errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: license requires attribution but no credit line is recorded (32 §14)."));
        }
    }

    private static void ValidateAudio(AudioEventDef e, string rel, Mark m, string repoRoot, List<ContentError> errors)
    {
        if (e.Files.Count == 0) { errors.Add(new(rel, m.Line, m.Column, $"{e.Id}: needs at least one file.")); }
        foreach (var f in e.Files)
        {
            if (!File.Exists(Path.Combine(repoRoot, f))) { errors.Add(new(rel, m.Line, m.Column, $"{e.Id}: file '{f}' does not exist.")); }
        }

        if (e.PitchJitter is < 0 or > 0.5f || e.VolumeJitterDb is < 0 or > 12)
        {
            errors.Add(new(rel, m.Line, m.Column, $"{e.Id}: jitter out of range (pitch 0–0.5, volume 0–12 dB)."));
        }
    }

    private static void ValidateNeed(NeedDef n, string rel, Mark m, List<ContentError> errors)
    {
        if (n.Kind == NeedKind.Physical && n.DecayPerHour is null)
        {
            errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{n.Id}: physical needs require decay_per_hour."));
        }

        if (n.DecayPerHour is { } d && new[] { d.Sleep, d.Rest, d.Light, d.Moderate, d.Heavy }.Any(v => v is < 0 or > 100))
        {
            errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{n.Id}: decay rates must be within 0–100 per hour."));
        }
    }

    private static void CheckCanonical(string what, IReadOnlyList<string> canon, IEnumerable<string> actual, List<ContentError> errors)
    {
        var have = actual.ToHashSet(StringComparer.Ordinal);
        var missing = canon.Where(c => !have.Contains(c)).ToArray();
        var extra = have.Where(h => !canon.Contains(h)).OrderBy(h => h, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0) { errors.Add(new(what, 0, 0, $"Missing canonical {what} (canon §10): {string.Join(", ", missing)}")); }
        if (extra.Length > 0) { errors.Add(new(what, 0, 0, $"Non-canonical {what} (change canon first): {string.Join(", ", extra)}")); }
    }

    private static ulong Hash(IEnumerable<SkillDef> skills, IEnumerable<ItemDef> items, IEnumerable<NeedDef> needs)
    {
        var h = new XxHash64();
        foreach (var d in skills) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in items) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in needs) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        return h.GetCurrentHashAsUInt64();
    }
}
