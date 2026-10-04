using System.Text.Json;
using System.IO;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Poster.Core;

public class Workspace
{
    private static readonly Regex VariablePattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    public Dictionary<string, string> Variables { get; set; } = new();
    public string Name { get; set; } = "My Workspace";
    public Guid Id { get; set; }

    [JsonIgnore]
    public HttpClient Client { get; set; }

    public List<PosterReqRes> Requests { get; set; } = [];

    public Workspace()
    {
        Id = Guid.CreateVersion7();
        Client = new HttpClient();
    }

    // Resolves {{variable}} placeholders in a string using Variables dictionary
    public string Resolve(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return VariablePattern.Replace(input, m =>
            Variables.TryGetValue(m.Groups[1].Value, out string? val) ? val : m.Value);
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(this, PosterJsonContext.Default.Workspace);
        File.WriteAllText($"Workspace-{Id}.json", json);
    }

    public string[] ListLocal()
    {
        return Directory.GetFiles(Directory.GetCurrentDirectory(), "Workspace*.json");
    }

    public void Load(string filePath)
    {
        if (!File.Exists(filePath)) return;
        string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        if (json.Length <= 10) return;
        var w = JsonSerializer.Deserialize(json, PosterJsonContext.Default.Workspace);
        if (w != null) load(w);
    }

    /// <summary>
    /// Imports requests from an OpenAPI 3.x spec loaded from a local JSON file.
    /// </summary>
    public void ImportFromOpenApiFile(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("OpenAPI file not found.", filePath);
        string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        ImportOpenApiJson(json);
    }

    /// <summary>
    /// Imports requests from an OpenAPI 3.x spec fetched from a URL.
    /// Blocks synchronously — call from a background context or wrap in Task.Run if needed.
    /// </summary>
    public void ImportFromOpenApiUrl(string url)
    {
        using var http = new HttpClient();
        string json = http.GetStringAsync(url).GetAwaiter().GetResult();
        ImportOpenApiJson(json);
    }

    // ── OpenAPI parser ────────────────────────────────────────────────────────

    private void ImportOpenApiJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Derive a base URL from the first server entry, if present, and store as a variable
        if (root.TryGetProperty("servers", out var servers) &&
            servers.ValueKind == JsonValueKind.Array &&
            servers.GetArrayLength() > 0)
        {
            string serverUrl = servers[0].GetProperty("url").GetString() ?? "";
            if (!string.IsNullOrEmpty(serverUrl))
                Variables.TryAdd("base_url", serverUrl.TrimEnd('/'));
        }

        if (!root.TryGetProperty("paths", out var paths)) return;

        foreach (var pathProp in paths.EnumerateObject())
        {
            string path = pathProp.Name; // e.g. "/users/{id}"
            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                string methodStr = methodProp.Name.ToUpperInvariant();
                if (!TryParseMethod(methodStr, out var method)) continue;

                var op = methodProp.Value;
                string summary = op.TryGetProperty("summary", out var s) ? s.GetString() ?? path : path;

                var req = new PosterReqRes(this)
                {
                    Name = summary,
                    Route = "{{base_url}}" + path,
                    HttpMethod = method,
                };

                // Pull a request-body example if present
                if (op.TryGetProperty("requestBody", out var body) &&
                    body.TryGetProperty("content", out var content))
                {
                    foreach (var mediaType in content.EnumerateObject())
                    {
                        if (mediaType.Value.TryGetProperty("example", out var example))
                        {
                            req.Body = example.GetRawText();
                            break;
                        }
                        if (mediaType.Value.TryGetProperty("schema", out _))
                        {
                            req.Body = "{}"; // placeholder — no example provided
                            break;
                        }
                    }
                }

                Requests.Add(req);
            }
        }
    }

    private static bool TryParseMethod(string s, out HttpMethod method)
    {
        method = s switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "PATCH" => HttpMethod.Patch,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            _ => HttpMethod.Get,
        };
        return s is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS";
    }

    private void load(Workspace w)
    {
        Id = w.Id;
        Name = w.Name;
        Variables = w.Variables;
        Requests = w.Requests;
        foreach (var req in Requests)
            req.SetParent(this);
    }
}
