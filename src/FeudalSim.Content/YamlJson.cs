using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace FeudalSim.Content;

/// <summary>
/// Converts YamlDotNet nodes to JSON (for schema validation and binding) while remembering where every
/// value came from, so schema errors can be reported as file:line:column.
/// </summary>
internal static class YamlJson
{
    public static JsonNode? Convert(YamlNode node, string pointer, Dictionary<string, Mark> marks)
    {
        marks[pointer] = node.Start;
        switch (node)
        {
            case YamlMappingNode map:
                var obj = new JsonObject();
                foreach (var (key, value) in map.Children)
                {
                    var name = ((YamlScalarNode)key).Value ?? "";
                    obj[name] = Convert(value, $"{pointer}/{Escape(name)}", marks);
                }

                return obj;

            case YamlSequenceNode seq:
                var arr = new JsonArray();
                for (var i = 0; i < seq.Children.Count; i++)
                {
                    arr.Add(Convert(seq.Children[i], $"{pointer}/{i}", marks));
                }

                return arr;

            case YamlScalarNode scalar:
                return Scalar(scalar);

            default:
                return null;
        }
    }

    private static JsonNode? Scalar(YamlScalarNode scalar)
    {
        var text = scalar.Value ?? "";
        if (scalar.Style is not ScalarStyle.Plain) { return JsonValue.Create(text); }
        if (text is "" or "~" or "null") { return null; }
        if (text is "true") { return JsonValue.Create(true); }
        if (text is "false") { return JsonValue.Create(false); }
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { return JsonValue.Create(l); }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { return JsonValue.Create(d); }
        return JsonValue.Create(text);
    }

    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
