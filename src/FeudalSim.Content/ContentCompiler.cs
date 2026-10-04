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
        var claims = new List<(ClaimPredicateDef Def, string Rel, Mark Mark)>();
        var overheard = new List<(OverheardLineDef Def, string Rel, Mark Mark)>();
        var schedules = new List<ScheduleDef>();
        var decisions = new List<(DecisionDef Def, string Rel, Mark Mark)>();
        var lines = new List<LineTemplateDef>();
        var worldSpecs = new List<WorldSpecDef>();
        var flaws = new List<FlawDef>();
        var minigames = new List<MinigameDef>();
        var nodes = new List<NodeDef>();
        var diseases = new List<DiseaseDef>();
        var recipes = new List<(RecipeDef Def, string Rel, Mark Mark)>();
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
                        case SkillDef s:
                            if (s.Attributes is { } wts && (wts.Keys.Any(k => k is not ("str" or "end" or "dex" or "per" or "int" or "cha")) || Math.Abs(wts.Values.Sum() - 1f) > 0.01f))
                            {
                                errors.Add(new(rel, mark.Line, mark.Column, $"{s.Id}: attribute weights use str/end/dex/per/int/cha and sum to 1 (12 §4.2)."));
                            }

                            skills.Add(s);
                            break;
                        case ItemDef i: ValidateItem(i, rel, mark, errors); items.Add(i); break;
                        case NeedDef n: ValidateNeed(n, rel, mark, errors); needs.Add(n); break;
                        case AssetDef a: ValidateAsset(a, rel, mark, repoRoot, errors); assets.Add(a); break;
                        case AudioEventDef e: ValidateAudio(e, rel, mark, repoRoot, errors); audio.Add(e); break;
                        case TraitDef t: traits.Add((t, rel, mark)); break;
                        case CultureDef c:
                            foreach (var f in c.FemaleNames ?? Array.Empty<string>())
                            {
                                if (c.GivenNames?.Contains(f) != true) { errors.Add(new(rel, mark.Line, mark.Column, $"{c.Id}: female name '{f}' is not in given_names.")); }
                            }

                            cultures.Add((c, rel, mark));
                            break;
                        case ProfessionDef p: professions.Add((p, rel, mark)); break;
                        case ActionDef a: actionMarks.Add((a, rel, mark)); actions.Add(a); break;   // validated after skills load
                        case ScheduleDef d: ValidateSchedule(d, rel, mark, errors); schedules.Add(d); break;
                        case OpinionModifierDef o: ValidateOpinionModifier(o, rel, mark, errors); opinionMods.Add(o); break;
                        case ClaimPredicateDef c: claims.Add((c, rel, mark)); break;   // the ladder is checked once all are loaded
                        case OverheardLineDef o: ValidateOverheard(o, rel, mark, errors); overheard.Add((o, rel, mark)); break;
                        case DecisionDef d: decisions.Add((d, rel, mark)); break;
                        case WorldSpecDef w:
                            if (w.LandAreaKm2.Count != 2 || w.PeakM.Count != 2 || w.LandAreaKm2[0] > w.LandAreaKm2[1] || w.PeakM[0] > w.PeakM[1])
                            {
                                errors.Add(new(rel, mark.Line, mark.Column, $"{w.Id}: bands are [min, max]."));
                            }

                            worldSpecs.Add(w);
                            break;
                        case RecipeDef r: recipes.Add((r, rel, mark)); break;
                        case DiseaseDef dd:
                            if (dd.IncubationH.Count != 2 || dd.Stages.Count == 0 || dd.Stages.Any(st => st.Hours.Count != 2 || st.Hours[0] > st.Hours[1]))
                            {
                                errors.Add(new(rel, mark.Line, mark.Column, $"{dd.Id}: incubation_h and each stage's hours are [min, max]."));
                            }

                            foreach (var k in dd.Grave?.Mult.Keys ?? [])
                            {
                                if (k is not ("child" or "adult" or "elder" or "malnourished" or "starving")) { errors.Add(new(rel, mark.Line, mark.Column, $"{dd.Id}: grave mult '{k}' is not child, adult, elder, malnourished or starving.")); }
                            }

                            diseases.Add(dd);
                            break;
                        case NodeDef nd:
                            string[] biomeKeys = ["coast_dunes", "meadow", "broadleaf", "pine", "wetland", "river_valley", "hills_moor", "highland"];
                            if (nd.Density.Keys.Any(k => !biomeKeys.Contains(k)) || nd.Density.Values.Any(v => v < 0f || v > 2000f))
                            {
                                errors.Add(new(rel, mark.Line, mark.Column, $"{nd.Id}: density keys are 10 §4 biomes, values 0–2000 per ha."));
                            }

                            if (nd.Kind == NodeKind.Tree && nd.Sizes is not { Count: 4 }) { errors.Add(new(rel, mark.Line, mark.Column, $"{nd.Id}: trees give 4 size weights.")); }
                            if (nd.Seasons.Any(x => x is not ("spring" or "summer" or "autumn" or "winter"))) { errors.Add(new(rel, mark.Line, mark.Column, $"{nd.Id}: seasons are spring/summer/autumn/winter.")); }
                            nodes.Add(nd);
                            break;
                        case MinigameDef g:
                            if (g.Stages.Any(s => s.Bands.Count != 5 || s.Bands.Any(b => b.Count != 7 || b.Zip(b.Skip(1)).Any(p => p.First > p.Second))))
                            {
                                errors.Add(new(rel, mark.Line, mark.Column, $"{g.Id}: each stage has 5 bands of 7 ascending quantiles."));
                            }

                            minigames.Add(g);
                            break;   // checked once items, skills and flaws are loaded
                        case FlawDef f:
                            if (f.Cap is < 0 or > 100 || f.HiddenDifficulty is < 0 or > 100) { errors.Add(new(rel, mark.Line, mark.Column, $"{f.Id}: cap and hidden_difficulty are 0–100.")); }
                            flaws.Add(f);
                            break;
                        case LineTemplateDef l:
                            if (l.Variants.Count == 0) { errors.Add(new(rel, mark.Line, mark.Column, $"{l.Id}: at least one variant.")); }
                            lines.Add(l);
                            break;
                    }
                }
            }
        }

        CheckCanonical("skills", CanonLists.SkillIds, skills.Select(s => s.Id), errors);
        CheckCanonical("needs", CanonLists.NeedIds, needs.Select(n => n.Id), errors);
        var symmetricTraits = ValidatePeople(traits, cultures, professions, skills, errors);
        foreach (var (a, rel, mark) in actionMarks) { ValidateAction(a, rel, mark, skills, errors); }
        foreach (var (c, rel, mark) in claims) { ValidateClaim(c, rel, mark, claims.Select(x => x.Def.Id).ToHashSet(StringComparer.Ordinal), errors); }
        if (overheard.Count > 0) { CheckOverheardCoverage(overheard, errors); }
        foreach (var (d, rel, mark) in decisions) { ValidateDecision(d, rel, mark, symmetricTraits, errors); }
        CheckLineCoverage(decisions.Select(d => d.Def), lines, errors);
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
        var claimDefs = claims.Select(c => c.Def).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var overheardDefs = overheard.Select(o => o.Def).OrderBy(o => o.Id, StringComparer.Ordinal).ToList();
        var decisionDefs = decisions.Select(d => d.Def).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        lines.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        worldSpecs.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        flaws.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        foreach (var (r, rel, mark) in recipes) { ValidateRecipe(r, rel, mark, items, skills, flaws, errors); }
        if (errors.Count > 0) { return new Result(null, errors, files); }
        var recipeDefs = recipes.Select(r => r.Def).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        foreach (var g in minigames.Where(g => !recipeDefs.Any(r => r.Id == g.Recipe))) { errors.Add(new("minigames", 0, 0, $"{g.Id}: unknown recipe {g.Recipe}.")); }
        minigames.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        nodes.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        diseases.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        foreach (var it in items.Where(i => i.Toxin is { } t && !diseases.Any(d => d.Id == t))) { errors.Add(new("items", 0, 0, $"{it.Id}: unknown toxin {it.Toxin}.")); }
        foreach (var it in items.Where(i => i.Food is not null))   // 11 §10.3: three groups, shares summing to 1
        {
            var f = it.Food!;
            if (f.Groups.Keys.Any(k => k is not ("staple" or "protein" or "fresh")) || MathF.Abs(f.Groups.Values.Sum() - 1f) > 0.01f || f.Sat < 0f || f.RawMult is < 0f or > 1f || f.RawPoisonP is < 0f or > 1f)
            {
                errors.Add(new("items", 0, 0, $"{it.Id}: food groups are staple/protein/fresh summing to 1; sat ≥ 0; raw_mult and raw_poison_p in [0, 1]."));
            }
        }

        foreach (var nd in nodes.Where(nd => nd.Confusable is { } c && !nodes.Any(x => x.Id == c))) { errors.Add(new("nodes", 0, 0, $"{nd.Id}: unknown confusable {nd.Confusable}.")); }
        foreach (var nd in nodes.Where(nd => nd.Forage is { } f && !items.Any(i => i.Id == f.Item))) { errors.Add(new("nodes", 0, 0, $"{nd.Id}: unknown forage item {nd.Forage!.Item}.")); }
        if (nodes.Count > 65535) { errors.Add(new("nodes", 0, 0, "At most 65,535 node types.")); }
        if (flaws.Count > 64) { errors.Add(new("flaws", 0, 0, "At most 64 flaws (an instance holds them as a bit mask).")); return new Result(null, errors, files); }
        var hash = Hash(skills, items, needs, traitDefs, cultureDefs, professionDefs, actions, schedules, opinionMods, claimDefs, overheardDefs, decisionDefs, worldSpecs, flaws, recipeDefs, nodes, diseases);   // minigame curves are presentation calibration: not in the sim hash
        return new Result(new ContentDatabase(skills, items, needs, hash, assets, audio, traitDefs, cultureDefs, professionDefs, actions, schedules, opinionMods, claimDefs, overheardDefs, decisionDefs, lines, worldSpecs, flaws, recipeDefs, minigames, nodes, diseases), errors, files);
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

    private static void ValidateRecipe(RecipeDef r, string rel, Mark m, List<ItemDef> items, List<SkillDef> skills, List<FlawDef> flaws, List<ContentError> errors)
    {
        void Err(string msg) => errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{r.Id}: {msg}"));
        bool ItemExists(string id) => items.Any(i => i.Id == id);
        bool TagExists(string tag) => items.Any(i => i.Tags?.Contains(tag) == true);
        if (!ItemExists(r.Output.Item)) { Err($"unknown output item {r.Output.Item}."); }
        if (!skills.Any(s => s.Id == r.Skill)) { Err($"unknown skill {r.Skill}."); }
        if (r.Difficulty is < 0 or > 120) { Err("difficulty is 0–120."); }
        foreach (var t in r.Tools) { if (!TagExists(t.Tag)) { Err($"no item carries tool tag {t.Tag}."); } }
        foreach (var i in r.Inputs)
        {
            if ((i.Item is null) == (i.Tag is null)) { Err($"input {i.Slot} names an item or a tag (exactly one)."); }
            else if (i.Item is not null && !ItemExists(i.Item)) { Err($"input {i.Slot}: unknown item {i.Item}."); }
            else if (i.Tag is not null && !TagExists(i.Tag)) { Err($"input {i.Slot}: no item carries tag {i.Tag}."); }
        }

        foreach (var s in r.SalvageOnRuin) { if (!ItemExists(s.Item)) { Err($"unknown salvage item {s.Item}."); } }
        if (r.Inputs.Count > 0 && Math.Abs(r.Inputs.Sum(i => i.Weight) - 1f) > 0.01f) { Err("input weights sum to 1 (13 §5.2 M)."); }
        var scored = r.Stages.Where(s => s.Scored).ToList();
        if (scored.Count == 0 || Math.Abs(scored.Sum(s => s.Weight) - 1f) > 0.01f) { Err("scored stage weights sum to 1 (13 §5.2)."); }
        if (r.Stages.Count > 16) { Err("at most 16 stages."); }
        if (scored.Count(s => s.Signature) != 1) { Err("exactly one signature stage."); }
        foreach (var s in r.Stages)
        {
            if (s.Flaw is { } f && !flaws.Any(x => x.Id == f)) { Err($"stage {s.Id}: unknown flaw {f}."); }
            if (!s.Scored && s.DurationDays is null) { Err($"stage {s.Id}: passive and tend stages need duration_days."); }
            if (s.Scored && s.LaborMin <= 0f) { Err($"stage {s.Id}: active stages need labor_min."); }
        }
    }

    private static void ValidateItem(ItemDef i, string rel, Mark m, List<ContentError> errors)
    {
        if (i.MassKg <= 0) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: mass_kg must be > 0.")); }
        if (i.BaseValueF < 0) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: base_value_f must be ≥ 0 farthings.")); }
        if (i.Durability is <= 0) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: durability must be > 0 when set.")); }
        if (i.Wear is { } wear)
        {
            if (i.Category != ItemCategory.Clothing) { errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: only clothing can be worn.")); }
            if (wear.Ins is < 0f or > 20f || wear.WetRetention is < 0f or > 1f || wear.RainResist is < 0f or > 1f)
            {
                errors.Add(new(rel, (int)m.Line, (int)m.Column, $"{i.Id}: wear needs ins 0–20 and wet_retention / rain_resist 0–1."));
            }
        }
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
        IEnumerable<ActionDef> actions, IEnumerable<ScheduleDef> schedules, IEnumerable<OpinionModifierDef> opinionMods, IEnumerable<ClaimPredicateDef> claims, IEnumerable<OverheardLineDef> overheard,
        IEnumerable<DecisionDef> decisions, IEnumerable<WorldSpecDef>? worldSpecs = null, IEnumerable<FlawDef>? flaws = null, IEnumerable<RecipeDef>? recipes = null,
        IEnumerable<NodeDef>? nodes = null, IEnumerable<DiseaseDef>? diseases = null)
    {
        var h = new XxHash64();
        foreach (var d in worldSpecs ?? []) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }   // M2-01: world generation inputs
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
        foreach (var d in claims) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in overheard) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in decisions) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }
        foreach (var d in flaws ?? []) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }   // M2-09
        foreach (var d in recipes ?? []) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }   // M2-10
        foreach (var d in nodes ?? []) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }   // M2-01b-ii: node scattering input
        foreach (var d in diseases ?? []) { h.Append(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d, Json))); }   // M2-07a
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

    private static void ValidateClaim(ClaimPredicateDef c, string rel, Mark m, IReadOnlySet<string> ids, List<ContentError> errors)
    {
        if (c.Juiciness is < 0 or > 1 || c.JuicinessIfMarried is < 0 or > 1) { errors.Add(new(rel, m.Line, m.Column, $"{c.Id}: juiciness must be 0–1.")); }
        foreach (var k in c.Axes?.Keys ?? []) { if (!CanonLists.ReputationAxes.Contains(k)) { errors.Add(new(rel, m.Line, m.Column, $"{c.Id}: unknown axis '{k}' ({string.Join(", ", CanonLists.ReputationAxes)}).")); } }
        if (c.EscalatesTo is { } next && (!ids.Contains(next) || next == c.Id)) { errors.Add(new(rel, m.Line, m.Column, $"{c.Id}: escalates_to '{next}' is not another claim.")); }
        foreach (var slot in Slots(c.Phrase)) { if (slot is not ("subject" or "object")) { errors.Add(new(rel, m.Line, m.Column, $"{c.Id}: phrase slot '{{{slot}}}' must be {{subject}} or {{object}}.")); } }
        if (!c.Phrase.Contains("{subject}", StringComparison.Ordinal)) { errors.Add(new(rel, m.Line, m.Column, $"{c.Id}: phrase must name {{subject}}.")); }
    }

    /// <summary>Interaction kinds that overheard talk can voice (16 §5.2 as implemented: <c>InteractionSystem.Kind</c>).</summary>
    public static IReadOnlyList<string> InteractionKinds { get; } = [.. Enum.GetNames<Sim.Systems.InteractionSystem.Kind>().Select(n => n.ToLowerInvariant())];

    private static IEnumerable<string> Slots(string text)
        => System.Text.RegularExpressions.Regex.Matches(text, @"\{([a-z_]+)\}").Select(m => m.Groups[1].Value);

    private static void ValidateOverheard(OverheardLineDef o, string rel, Mark m, List<ContentError> errors)
    {
        if (!InteractionKinds.Contains(o.Interaction)) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: unknown interaction '{o.Interaction}' ({string.Join(", ", InteractionKinds)}).")); }
        var allowed = o.Interaction is "gossip" or "warn" ? new[] { "a", "b", "claim" } : ["a", "b"];
        foreach (var slot in Slots(o.Text)) { if (!allowed.Contains(slot)) { errors.Add(new(rel, m.Line, m.Column, $"{o.Id}: slot '{{{slot}}}' is not allowed here ({string.Join(", ", allowed)}).")); } }
    }

    /// <summary>Every interaction kind has a subtitle for success and for failure (a line with no success covers both): template mode must stay complete.</summary>
    private static void CheckOverheardCoverage(List<(OverheardLineDef Def, string Rel, Mark Mark)> lines, List<ContentError> errors)
    {
        foreach (var kind in InteractionKinds)
        {
            foreach (var outcome in new[] { true, false })
            {
                if (!lines.Any(l => l.Def.Interaction == kind && (l.Def.Success is null || l.Def.Success == outcome)))
                {
                    var (_, rel, mark) = lines[0];
                    errors.Add(new(rel, mark.Line, mark.Column, $"overheard lines: no subtitle for '{kind}' ({(outcome ? "success" : "failure")})."));
                }
            }
        }
    }

    /// <summary>Canon §13.5: every option of every decision menu has a template line (only when templates exist at all).</summary>
    private static void CheckLineCoverage(IEnumerable<DecisionDef> decisions, List<LineTemplateDef> lines, List<ContentError> errors)
    {
        if (lines.Count == 0) { return; }
        var covered = lines.Select(l => l.Option).ToHashSet(StringComparer.Ordinal);
        foreach (var d in decisions)
        {
            foreach (var o in d.Options)
            {
                if (!covered.Contains(o.Id)) { errors.Add(new("lines", 0, 0, $"{d.Id}/{o.Id} has no template line (canon §13.5: template mode must stay playable).")); }
            }
        }
    }

    /// <summary>Unique option ids; facets, values, emotions and needs from canon; trait utility groups that some trait uses.</summary>
    private static void ValidateDecision(DecisionDef d, string rel, Mark m, IReadOnlyList<TraitDef> traits, List<ContentError> errors)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var groups = traits.SelectMany(t => t.Effects?.Utility?.Keys ?? Enumerable.Empty<string>()).ToHashSet(StringComparer.Ordinal);
        foreach (var o in d.Options)
        {
            void Err(string msg) => errors.Add(new(rel, m.Line, m.Column, $"{d.Id}/{o.Id}: {msg}"));
            if (!ids.Add(o.Id)) { Err("duplicate option id."); }
            if (o.FacetK is { } fk)
            {
                if (fk.Count != 1) { Err("facet_k takes exactly one facet."); }
                foreach (var f in fk.Keys) { if (!CanonLists.Facets.Contains(f)) { Err($"unknown facet '{f}'."); } }
            }

            foreach (var v in o.ValueTags?.Keys ?? Enumerable.Empty<string>()) { if (!CanonLists.Values.Contains(v)) { Err($"unknown value '{v}'."); } }
            if (o.Outlet is { } e && !CanonLists.Emotions.Contains(e)) { Err($"unknown emotion '{e}'."); }
            if (o.Need is { } n && !CanonLists.NeedIds.Contains("need." + n)) { Err($"unknown need '{n}'."); }
            if (o.UtilityKey is { } g && !groups.Contains(g)) { Err($"no trait uses utility group '{g}'."); }
            if (o.Direction is < -1 or > 1) { Err("direction is −1, 0 or +1."); }
        }
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
