using FeudalSim.Sim.Content;

namespace FeudalSim.Content.Tests;

/// <summary>M0-07 (20 §20 step 7): content v0.</summary>
public class ContentPipelineTests
{
    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln"))) { return dir.FullName; }
        }

        throw new DirectoryNotFoundException("Repo root not found.");
    }

    private static string ContentRoot => Path.Combine(RepoRoot(), "content");

    [Fact]
    public void Repository_content_compiles_cleanly()
    {
        var result = ContentCompiler.Compile(ContentRoot);
        result.Errors.ShouldBeEmpty(string.Join("\n", result.Errors));
        result.Database.ShouldNotBeNull();
        result.Database!.Items.Count.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void Skill_ids_equal_canon_10_2_exactly()
    {
        var db = ContentCompiler.Compile(ContentRoot).Database!;
        db.Skills.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(CanonLists.SkillIds.OrderBy(x => x, StringComparer.Ordinal));
        db.Skills.Count.ShouldBe(28);
        db.Needs.Select(n => n.Id).OrderBy(x => x, StringComparer.Ordinal)
            .ShouldBe(CanonLists.NeedIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Handles_follow_ordinal_id_order()
    {
        var db = ContentCompiler.Compile(ContentRoot).Database!;
        db.Items.Select(i => i.Id).ShouldBe(db.Items.Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void A_broken_fixture_fails_with_file_and_line()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "fixtures", "broken");
        var result = ContentCompiler.Compile(root);

        result.Ok.ShouldBeFalse();
        var text = string.Join("\n", result.Errors);
        // Schema error on base_value_f: "lots", line 10 of the fixture.
        result.Errors.ShouldContain(e => e.File == "items/bad.yaml" && e.Line == 10 && e.Message.Contains("/1/base_value_f"), text);
    }

    [Fact]
    public void Id_prefix_must_match_the_folder_kind()
    {
        var root = CopyContent();
        var tools = Path.Combine(root, "items", "tools.yaml");
        var index = File.ReadAllLines(tools).Count(l => l.StartsWith("- ", StringComparison.Ordinal));   // the appended entry's position
        File.AppendAllText(tools,
            "- { id: skill.misplaced, name: Misplaced, category: tool, tier: t0, trade_unit: each, mass_kg: 1, base_value_f: 1 }\n");
        var result = ContentCompiler.Compile(root);
        // The schema's id pattern (^item\.…) rejects it first; the compiler's prefix check is the backstop.
        result.Errors.ShouldContain(e => e.File == "items/tools.yaml" && e.Message.Contains($"/{index}/id") && e.Message.Contains("pattern"),
            string.Join("\n", result.Errors));
    }

    [Fact]
    public void Duplicate_ids_are_reported_with_both_locations()
    {
        var root = CopyContent();
        File.AppendAllText(Path.Combine(root, "items", "raw.yaml"),
            "- { id: item.iron_axe, name: Copy, category: tool, tier: t3, trade_unit: each, mass_kg: 1, base_value_f: 1 }\n");
        var result = ContentCompiler.Compile(root);
        result.Errors.ShouldContain(e => e.Message.Contains("Duplicate id 'item.iron_axe'"));
    }

    [Fact]
    public void Content_hash_is_stable_and_changes_with_content()
    {
        var a = ContentCompiler.Compile(ContentRoot).Database!.Hash;
        var b = ContentCompiler.Compile(ContentRoot).Database!.Hash;
        a.ShouldBe(b);

        var root = CopyContent(includeAssets: false);   // asset and audio files are repo-relative and don't exist in a temp copy
        var food = Path.Combine(root, "items", "food.yaml");
        File.WriteAllText(food, File.ReadAllText(food).Replace("base_value_f: 3, food:", "base_value_f: 4, food:", StringComparison.Ordinal));
        ContentCompiler.Compile(root).Database!.Hash.ShouldNotBe(a);
    }

    [Fact]
    public void Asset_outputs_must_exist_and_the_license_file_is_fresh()
    {
        var db = ContentCompiler.Compile(ContentRoot).Database!;
        db.Assets.ShouldContain(a => a.Id == "asset.flora.pine_a" && a.Status == AssetStatus.Review);
        File.ReadAllText(Path.Combine(RepoRoot(), "ASSET_LICENSES.md")).ShouldBe(AssetLicenses.Render(db.Assets),
            "ASSET_LICENSES.md is stale; run `feudalsim content licenses`.");

        var root = CopyContent();
        File.AppendAllText(Path.Combine(root, "assets", "flora.yaml"), """
            - id: asset.flora.ghost
              kind: model
              status: draft
              milestone: M0
              source: { type: hand }
              outputs: [game/assets/flora/ghost.glb]
              license: { name: CC0-1.0, author: Nobody, attribution_required: false }
            """.Replace("            ", "", StringComparison.Ordinal));
        var result = ContentCompiler.Compile(root);
        result.Errors.ShouldContain(e => e.Message.Contains("output 'game/assets/flora/ghost.glb' does not exist"));
    }

    [Fact]
    public void Overheard_lines_cover_every_interaction_and_use_known_slots()
    {
        var db = ContentCompiler.Compile(ContentRoot).Database!;
        db.OverheardLines.Select(l => l.Interaction).Distinct().Count().ShouldBe(ContentCompiler.InteractionKinds.Count);

        var root = CopyContent(includeAssets: false);
        var lines = Path.Combine(root, "social", "overheard_lines.yaml");
        var text = File.ReadAllLines(lines).Where(l => !l.Contains("interaction: warn", StringComparison.Ordinal)).ToList();
        text.Add("- { id: overheard.bad_slot, interaction: chat, text: \"{a} mentions {claim}.\" }");
        File.WriteAllLines(lines, text);
        var result = ContentCompiler.Compile(root);
        result.Errors.ShouldContain(e => e.Message.Contains("no subtitle for 'warn' (success)"), string.Join("\n", result.Errors));
        result.Errors.ShouldContain(e => e.Message.Contains("overheard.bad_slot: slot '{claim}' is not allowed"));
    }

    [Fact]
    public void Claim_phrases_must_name_the_subject()
    {
        var root = CopyContent(includeAssets: false);
        var claims = Path.Combine(root, "social", "claim_predicates.yaml");
        File.WriteAllText(claims, File.ReadAllText(claims).Replace("\"{subject} is devout\"", "\"{someone} is devout\"", StringComparison.Ordinal));
        var result = ContentCompiler.Compile(root);
        result.Errors.ShouldContain(e => e.Message.Contains("claim.devout: phrase slot '{someone}'"));
        result.Errors.ShouldContain(e => e.Message.Contains("claim.devout: phrase must name {subject}"));
    }

    [Fact]
    public void Committed_schemas_are_fresh()
    {
        foreach (var (_, kind, type) in SchemaGenerator.Kinds)
        {
            var path = Path.Combine(ContentRoot, "schemas", $"{kind}.schema.json");
            File.Exists(path).ShouldBeTrue($"{path} is missing; run `feudalsim content schemas`.");
            File.ReadAllText(path).ShouldBe(SchemaGenerator.Generate(kind, type), $"{kind}.schema.json is stale; run `feudalsim content schemas`.");
        }
    }

    private static string CopyContent(bool includeAssets = true)
    {
        var dest = Path.Combine(Path.GetTempPath(), "feudalsim-content-tests", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.GetFiles(ContentRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(ContentRoot, file);
            if (!includeAssets && (rel.StartsWith("assets", StringComparison.Ordinal) || rel.StartsWith("audio", StringComparison.Ordinal))) { continue; }
            var target = Path.Combine(dest, Path.GetRelativePath(ContentRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return dest;
    }
}
