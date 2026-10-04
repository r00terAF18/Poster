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

    /// <summary>Per-user folder where workspaces are saved (e.g. %AppData%/Poster).</summary>
    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Poster");

    /// <summary>Saves this workspace into <see cref="DefaultDirectory"/> and returns the file path.</summary>
    public string Save()
    {
        Directory.CreateDirectory(DefaultDirectory);
        string path = Path.Combine(DefaultDirectory, $"{Name}-{Id}.json");
        string json = JsonSerializer.Serialize(this, PosterJsonContext.Default.Workspace);
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Reads just the workspace name from a saved file, or null if it cannot be read.</summary>
    public static string? ReadName(string filePath)
    {
        try
        {
            using FileStream stream = File.OpenRead(filePath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            return doc.RootElement.TryGetProperty("Name", out JsonElement name) &&
                   name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public string[] ListLocal()
    {
        HashSet<string> files = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(DefaultDirectory))
            foreach (string file in Directory.EnumerateFiles(DefaultDirectory, "Workspace*.json"))
                files.Add(file);

        // Legacy locations (older versions saved next to the working directory).
        AddWorkspaceFiles(Directory.GetCurrentDirectory(), files);
        AddWorkspaceFiles(AppContext.BaseDirectory, files);
        return files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddWorkspaceFiles(string startDirectory, ISet<string> files)
    {
        DirectoryInfo? directory = new(startDirectory);
        while (directory != null)
        {
            foreach (string file in Directory.EnumerateFiles(directory.FullName, "Workspace*.json"))
                files.Add(file);

            directory = directory.Parent;
        }
    }

    public void Load(string filePath)
    {
        if (!File.Exists(filePath)) return;
        string json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
        if (json.Length <= 10) return;
        Workspace? w = JsonSerializer.Deserialize(json, PosterJsonContext.Default.Workspace);
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

    /// <summary>Imports requests from an OpenAPI 3.x spec fetched from a URL.</summary>
    public async Task ImportFromOpenApiUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        Uri specUri = new(url, UriKind.Absolute);
        string json = await Client.GetStringAsync(specUri, cancellationToken);
        ImportOpenApiJson(json, specUri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>Blocking variant kept for the console client. Prefer <see cref="ImportFromOpenApiUrlAsync"/>.</summary>
    [Obsolete("Blocks the calling thread; use ImportFromOpenApiUrlAsync.")]
    public void ImportFromOpenApiUrl(string url) =>
        ImportFromOpenApiUrlAsync(url).GetAwaiter().GetResult();

    // ── OpenAPI parser ────────────────────────────────────────────────────────

    private void ImportOpenApiJson(string json, string? fallbackBaseUrl = null)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        // Prefer the first declared server, falling back to the source URL's origin.
        string? baseUrl = fallbackBaseUrl;
        if (root.TryGetProperty("servers", out JsonElement servers) &&
            servers.ValueKind == JsonValueKind.Array &&
            servers.GetArrayLength() > 0)
        {
            JsonElement firstServer = servers[0];
            if (firstServer.ValueKind == JsonValueKind.Object &&
                firstServer.TryGetProperty("url", out JsonElement serverUrlElement) &&
                serverUrlElement.ValueKind == JsonValueKind.String)
            {
                string? serverUrl = serverUrlElement.GetString();
                if (!string.IsNullOrWhiteSpace(serverUrl))
                    baseUrl = serverUrl;
            }
        }

        if (!string.IsNullOrWhiteSpace(baseUrl))
            Variables.TryAdd("base_url", baseUrl.TrimEnd('/'));

        if (!root.TryGetProperty("paths", out JsonElement paths)) return;

        foreach (JsonProperty pathProp in paths.EnumerateObject())
        {
            string path = pathProp.Name; // e.g. "/users/{id}"
            foreach (JsonProperty methodProp in pathProp.Value.EnumerateObject())
            {
                string methodStr = methodProp.Name.ToUpperInvariant();
                if (!TryParseMethod(methodStr, out HttpMethod method)) continue;

                JsonElement op = methodProp.Value;
                string summary = op.TryGetProperty("summary", out JsonElement s) ? s.GetString() ?? path : path;

                PosterReqRes req = new PosterReqRes(this)
                {
                    Name = summary,
                    Route = "{{base_url}}" + path,
                    HttpMethod = method,
                };

                // Pull a request-body example if present
                if (op.TryGetProperty("requestBody", out JsonElement body) &&
                    body.TryGetProperty("content", out JsonElement content))
                {
                    foreach (JsonProperty mediaType in content.EnumerateObject())
                    {
                        req.Headers.TryAdd("Content-Type", mediaType.Name);
                        if (mediaType.Value.TryGetProperty("example", out JsonElement example))
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
        foreach (PosterReqRes req in Requests)
            req.SetParent(this);
    }
}
