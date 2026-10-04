using System.ComponentModel;
using FeudalSim.Content;
using Spectre.Console.Cli;

namespace FeudalSim.Headless.Commands;

public class ContentSettings : CommandSettings
{
    [CommandOption("--root <PATH>")]
    [Description("Content folder (default: ./content found from the current directory upward).")]
    public string? Root { get; init; }

    public string ResolveRoot() => Root ?? RepoPaths.FindContentRoot(Directory.GetCurrentDirectory());
}

public sealed class ContentValidateCommand : Command<ContentSettings>
{
    public override int Execute(CommandContext context, ContentSettings settings, CancellationToken cancellationToken)
    {
        var root = settings.ResolveRoot();
        var result = ContentCompiler.Compile(root);
        foreach (var e in result.Errors) { Console.Error.WriteLine($"content/{e}"); }
        if (!result.Ok)
        {
            Console.Error.WriteLine($"content: FAILED with {result.Errors.Count} error(s) in {result.FileCount} file(s).");
            return 1;
        }

        var db = result.Database!;
        Console.WriteLine($"content: OK — {result.FileCount} files, {db.Skills.Count} skills, {db.Needs.Count} needs, {db.Items.Count} items, {db.Traits.Count} traits, {db.Cultures.Count} cultures, {db.Professions.Count} professions; hash {db.Hash:x16}");
        return 0;
    }
}

public sealed class ContentSchemasSettings : ContentSettings
{
    [CommandOption("--check")]
    [Description("Fail if committed schemas differ from the generated ones (CI).")]
    public bool Check { get; init; }
}

public sealed class ContentSchemasCommand : Command<ContentSchemasSettings>
{
    public override int Execute(CommandContext context, ContentSchemasSettings settings, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(settings.ResolveRoot(), "schemas");
        Directory.CreateDirectory(dir);
        var stale = 0;
        foreach (var (_, kind, type) in SchemaGenerator.Kinds)
        {
            var path = Path.Combine(dir, $"{kind}.schema.json");
            var generated = SchemaGenerator.Generate(kind, type);
            var current = File.Exists(path) ? File.ReadAllText(path) : null;
            if (current == generated) { continue; }
            if (settings.Check)
            {
                Console.Error.WriteLine($"schemas: STALE {Path.GetFileName(path)} — run `feudalsim content schemas`.");
                stale++;
            }
            else
            {
                File.WriteAllText(path, generated);
                Console.WriteLine($"schemas: wrote {Path.GetFileName(path)}");
            }
        }

        if (settings.Check && stale == 0) { Console.WriteLine("schemas: up to date"); }
        return stale == 0 ? 0 : 1;
    }
}

public static class RepoPaths
{
    public static string FindContentRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FeudalSim.sln")) && Directory.Exists(Path.Combine(dir.FullName, "content")))
            {
                return Path.Combine(dir.FullName, "content");
            }
        }

        throw new DirectoryNotFoundException("Could not find the repo's content/ folder; pass --root.");
    }
}

public sealed class ContentLicensesCommand : Command<ContentSchemasSettings>
{
    public override int Execute(CommandContext context, ContentSchemasSettings settings, CancellationToken cancellationToken)
    {
        var root = settings.ResolveRoot();
        var compiled = ContentCompiler.Compile(root);
        if (!compiled.Ok)
        {
            foreach (var e in compiled.Errors) { Console.Error.WriteLine($"content/{e}"); }
            return 1;
        }

        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar))!, "ASSET_LICENSES.md");
        var generated = AssetLicenses.Render(compiled.Database!.Assets);
        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        if (settings.Check)
        {
            Console.WriteLine(current == generated ? "licenses: up to date" : "licenses: STALE — run `feudalsim content licenses`.");
            return current == generated ? 0 : 1;
        }

        File.WriteAllText(path, generated);
        Console.WriteLine($"licenses: wrote ASSET_LICENSES.md ({compiled.Database!.Assets.Count} assets)");
        return 0;
    }
}
