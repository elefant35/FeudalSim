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
        var traits = new List<(TraitDef Def, string Rel, Mark Mark)>();
        var cultures = new List<(CultureDef Def, string Rel, Mark Mark)>();
        var professions = new List<(ProfessionDef Def, string Rel, Mark Mark)>();
        var actions = new List<ActionDef>();
        var actionMarks = new List<(ActionDef Def, string Rel, Mark Mark)>();
        var opinionMods = new List<OpinionModifierDef>();
        var schedules = new List<ScheduleDef>();
        var repoRoot = Path.GetDirectoryName(Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar)) ?? contentRoot;
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = 0;

        foreach (var (folder, kind, type) in SchemaGenerator.Kinds)
        {
            var schema = Schemas[kind];
            // A kind owns a whole folder, or one named file in a shared folder ("social/opinion_modifiers.yaml").
            var single = folder.EndsWith(".yaml", StringComparison.Ordinal);
            var dir = Path.Combine(contentRoot, single ? Path.GetDirectoryName(folder)! : folder);
            if (!Directory.Exists(dir)) { continue; }
            var candidates = single ? Directory.GetFiles(dir, Path.GetFileName(folder)) : Directory.GetFiles(dir, "*.yaml");
            foreach (var file in candidates.OrderBy(f => f, StringComparer.Ordinal))
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
                        case TraitDef t: traits.Add((t, rel, mark)); break;
                        case CultureDef c: cultures.Add((c, rel, mark)); break;
                        case ProfessionDef p: professions.Add((p, rel, mark)); break;
                        case ActionDef a: actionMarks.Add((a, rel, mark)); actions.Add(a); break;   // validated after skills load
                        case ScheduleDef d: ValidateSchedule(d, rel, mark, errors); schedules.Add(d); break;
                        case OpinionModifierDef o: ValidateOpinionModifier(o, rel, mark, errors); opinionMods.Add(o); break;
                    }
                }
            }
        }

        CheckCanonical("skills", CanonLists.SkillIds, skills.Select(s => s.Id), errors);
        CheckCanonical("needs", CanonLists.NeedIds, needs.Select(n => n.Id), errors);
        var symmetricTraits = ValidatePeople(traits, cultures, professions, skills, errors);
        foreach (var (a, rel, mark) in actionMarks) { ValidateAction(a, rel, mark, skills, errors); }
        if (errors.Count > 0) { return new Result(null, errors, files); }

        skills.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        items.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        needs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        assets.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        audio.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var traitDefs = symmetricTraits.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var cultureDefs = cultures.Select(c => c.Def).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var professionDefs = professions.Select(p => p.Def).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        actions.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        opinionMods.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        schedules.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        var hash = Hash(skills, items, needs, traitDefs, cultureDefs, professionDefs, actions, schedules, opinionMods);
        return new Result(new ContentDatabase(skills, items, needs, hash, assets, audio, traitDefs, cultureDefs, professionDefs, actions, schedules, opinionMods), errors, files);
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

    private static ulong Hash(IEnumerable<SkillDef> skills, IEnumerable<ItemDef> items, IEnumerable<NeedDef> needs,
        IEnumerable<TraitDef> traits, IEnumerable<CultureDef> cultures, IEnumerable<ProfessionDef> professions,
        IEnumerable<ActionDef> actions, IEnumerable<ScheduleDef> schedules, IEnumerable<OpinionModifierDef> opinionMods)
    {
        var h = new XxHash64();
        foreach (var d in skills) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in items) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in needs) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }

        // People content (M1-01): appended only when present, so content without it keeps its earlier hash.
        foreach (var d in traits) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in cultures) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in professions) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in actions) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in schedules) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in opinionMods) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        return h.GetCurrentHashAsUInt64();
    }

    /// <summary>Camp stocks an action may read or change (M1 graybox camp), and the needs it may touch.</summary>
    public static readonly IReadOnlyList<string> CampStocks = ["food", "firewood", "fire_fuel_min", "threat"];

    private static readonly string[] ScheduleBlocks = ["sleep", "morning", "meal", "work", "social", "worship", "market", "obligation", "any"];

    private static void ValidateAction(ActionDef a, string rel, Mark m, List<SkillDef> skills, List<ContentError> errors)
    {
        var needs = CanonLists.NeedIds.Select(n => n["need.".Length..]).ToArray();
        void Keys(IEnumerable<string>? keys, IReadOnlyCollection<string> allowed, string what)
        {
            foreach (var k in keys ?? []) { if (!allowed.Contains(k)) { errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: unknown {what} '{k}' ({string.Join(", ", allowed)}).")); } }
        }

        Keys(a.WeightNeed is null ? null : [a.WeightNeed], needs, "need");
        Keys(a.NeedPerHour?.Keys, needs, "need");
        Keys(a.UntilNeed?.Keys, needs, "need");
        Keys(a.StartBelow?.Keys, needs, "need");
        Keys(a.StockPerHour?.Keys, CampStocks.ToArray(), "camp stock");
        Keys(a.Requires?.Keys, CampStocks.ToArray(), "camp stock");
        Keys(a.Consumes is null ? null : [a.Consumes.Stock], CampStocks.ToArray(), "camp stock");
        Keys(a.StockPressure is null ? null : [a.StockPressure.Stock], CampStocks.ToArray(), "camp stock");
        Keys(a.FacetK?.Keys, CanonLists.Facets.ToArray(), "facet");
        Keys([a.ScheduleBlock], ScheduleBlocks, "schedule block");
        if (a.Skill is not null && skills.All(s => s.Id != a.Skill)) { errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: unknown skill '{a.Skill}'.")); }
        if (a.DurationMin <= 0) { errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: duration_min must be > 0.")); }
        if (a.StockPressure is { Comfortable: <= 0 }) { errors.Add(new(rel, m.Line, m.Column, $"{a.Id}: stock_pressure.comfortable must be > 0.")); }
    }

    private static void ValidateOpinionModifier(OpinionModifierDef o, string rel, Mark m, List<ContentError> errors)
    {
        if (o.HalfLifeDays <= 0) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: half_life_days must be > 0.")); }
        if (o.Cap is { } cap && Math.Sign(cap) != Math.Sign(o.Value)) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: cap must have the sign of value.")); }
        if (o.Stacking is OpinionStacking.Add or OpinionStacking.Saturate && o.Cap is null) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: {o.Stacking} needs a cap.")); }
        if (o.FloorFraction is < 0 or > 1) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: floor_fraction must be 0–1.")); }
        foreach (var k in o.ExtendsTo?.Keys ?? []) { if (k is not ("household" or "kin" or "spouse")) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: extends_to '{k}' must be household, kin or spouse.")); } }
    }

    private static void ValidateSchedule(ScheduleDef d, string rel, Mark m, List<ContentError> errors)
    {
        foreach (var b in d.Blocks)
        {
            if (!TimeOnly.TryParseExact(b.From, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
                || !TimeOnly.TryParseExact(b.To, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            {
                errors.Add(new(rel, m.Line, m.Column, $"{d.Id}: block times must be HH:mm (got {b.From}–{b.To})."));
            }

            if (!ScheduleBlocks.Contains(b.Block) || b.Block == "any") { errors.Add(new(rel, m.Line, m.Column, $"{d.Id}: unknown block '{b.Block}'.")); }
        }
    }

    /// <summary>
    /// Cross-checks the people content (M1-01): referenced trait/skill ids exist; facet, value, emotion and need keys
    /// are canonical; multipliers are positive; at most 64 traits (a person's trait set is a 64-bit set). Returns the
    /// traits with incompatibility made symmetric (A lists B ⇒ B excludes A).
    /// </summary>
    private static List<TraitDef> ValidatePeople(List<(TraitDef Def, string Rel, Mark Mark)> traits, List<(CultureDef Def, string Rel, Mark Mark)> cultures,
        List<(ProfessionDef Def, string Rel, Mark Mark)> professions, List<SkillDef> skills, List<ContentError> errors)
    {
        var traitIds = traits.Select(t => t.Def.Id).ToHashSet(StringComparer.Ordinal);
        var skillIds = skills.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var needKeys = CanonLists.NeedIds.Select(n => n["need.".Length..]).ToHashSet(StringComparer.Ordinal);
        if (traits.Count > 64) { errors.Add(new("traits", 0, 0, $"{traits.Count} traits; a person's trait set holds at most 64.")); }

        void Keys(IEnumerable<string>? keys, IReadOnlyList<string> allowed, string what, string rel, Mark m)
        {
            foreach (var k in keys ?? []) { if (!allowed.Contains(k)) { errors.Add(new(rel, m.Line, m.Column, $"Unknown {what} '{k}' (canon §10: {string.Join(", ", allowed)}).")); } }
        }

        void Positive(IEnumerable<KeyValuePair<string, float>>? map, string what, string rel, Mark m)
        {
            foreach (var (k, v) in map ?? []) { if (!(v > 0)) { errors.Add(new(rel, m.Line, m.Column, $"{what} for '{k}' must be > 0 (got {v}).")); } }
        }

        var excludes = traits.ToDictionary(t => t.Def.Id, _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var (t, rel, m) in traits)
        {
            Keys(t.FacetAffinity?.Keys, CanonLists.Facets, "facet", rel, m);
            Keys(t.Effects?.EmotionGain?.Keys, CanonLists.Emotions, "emotion", rel, m);
            Keys(t.Effects?.HalfLife?.Keys, CanonLists.Emotions, "emotion", rel, m);
            Keys(t.Effects?.HijackThreshold?.Keys, CanonLists.Emotions, "emotion", rel, m);
            Keys(t.Effects?.NeedDecay?.Keys, [.. needKeys], "need", rel, m);
            Keys(t.Effects?.ValueFloor?.Keys, CanonLists.Values, "value", rel, m);
            Keys(t.Effects?.ValueCap?.Keys, CanonLists.Values, "value", rel, m);
            Keys(t.Effects?.ValueShift?.Keys, CanonLists.Values, "value", rel, m);
            Positive(t.Effects?.EmotionGain, "emotion_gain", rel, m);
            Positive(t.Effects?.HalfLife, "half_life", rel, m);
            Positive(t.Effects?.Utility, "utility", rel, m);
            Positive(t.Effects?.NeedDecay, "need_decay", rel, m);
            if (!(t.Prevalence > 0)) { errors.Add(new(rel, m.Line, m.Column, $"prevalence must be > 0 (got {t.Prevalence}).")); }
            foreach (var other in t.Incompatible ?? [])
            {
                if (other == t.Id) { errors.Add(new(rel, m.Line, m.Column, $"{t.Id} cannot be incompatible with itself.")); }
                else if (!traitIds.Contains(other)) { errors.Add(new(rel, m.Line, m.Column, $"Unknown trait '{other}' in incompatible.")); }
                else { excludes[t.Id].Add(other); excludes[other].Add(t.Id); }
            }
        }

        foreach (var (c, rel, m) in cultures)
        {
            Keys(c.ValueMeans?.Keys, CanonLists.Values, "value", rel, m);
            foreach (var (k, v) in c.ValueMeans ?? new Dictionary<string, int>()) { if (v is < 0 or > 100) { errors.Add(new(rel, m.Line, m.Column, $"value_means.{k} must be 0–100 (got {v}).")); } }
            foreach (var k in c.TraitMultipliers?.Keys ?? []) { if (!traitIds.Contains(k)) { errors.Add(new(rel, m.Line, m.Column, $"Unknown trait '{k}' in trait_multipliers.")); } }
            Positive(c.TraitMultipliers, "trait_multipliers", rel, m);
        }

        foreach (var (p, rel, m) in professions)
        {
            if (p.Primary.Count == 0) { errors.Add(new(rel, m.Line, m.Column, $"{p.Id} needs at least one primary skill.")); }
            foreach (var s in p.Primary.Concat(p.Secondary ?? [])) { if (!skillIds.Contains(s)) { errors.Add(new(rel, m.Line, m.Column, $"Unknown skill '{s}' in {p.Id}.")); } }
        }

        return [.. traits.Select(t => excludes[t.Def.Id].Count == 0 ? t.Def with { Incompatible = null } : t.Def with { Incompatible = [.. excludes[t.Def.Id]] })];
    }
}
