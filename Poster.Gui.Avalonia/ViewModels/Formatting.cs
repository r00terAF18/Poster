using System.Text;
using System.Text.Json;

namespace Poster.Desktop.ViewModels;

internal static class Formatting
{
    /// <summary>Parses "key=value" lines. Blank lines and lines without a key are skipped.</summary>
    public static Dictionary<string, string> ParsePairs(string text)
    {
        Dictionary<string, string> pairs = new();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;

            int split = line.IndexOf('=');
            string key = (split < 0 ? line : line[..split]).Trim();
            string value = split < 0 ? "" : line[(split + 1)..].Trim();
            if (key.Length > 0) pairs[key] = value;
        }

        return pairs;
    }

    public static string FormatPairs(Dictionary<string, string> pairs) =>
        string.Join("\n", pairs.Select(pair => $"{pair.Key}={pair.Value}"));

    public static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
    };

    /// <summary>Indents JSON for display; anything that isn't valid JSON is returned untouched.</summary>
    public static string PrettyBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";

        string trimmed = body.TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return body;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
                doc.WriteTo(writer);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
