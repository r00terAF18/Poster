using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;

namespace Poster.Core;

public enum AuthType { None, Bearer, Basic, ApiKey }

public class AuthConfig
{
    public AuthType Type { get; set; } = AuthType.None;

    // Bearer / ApiKey token value
    public string Token { get; set; } = "";

    // Basic auth
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    // ApiKey settings
    public string ApiKeyHeader { get; set; } = "X-API-Key";
}

public class PosterReqRes
{
    public required HttpMethod HttpMethod { get; set; } = HttpMethod.Get;
    public required string Route { get; set; }
    public string Name { get; set; } = "New Request";

    [JsonIgnore]
    public HttpRequestMessage? RequestMessage { get; private set; }
    [JsonIgnore]
    public HttpResponseMessage? ResponseMessage { get; private set; }

    /// <summary>Custom request headers. Supports {{variable}} interpolation in values.</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>Authentication configuration for this request.</summary>
    public AuthConfig Auth { get; set; } = new();

    public string? ResponseBody { get; private set; }
    public string Body { get; set; } = "";
    public string? Error { get; set; }
    public DateTime LastExec { get; set; }
    public Metrics Metric { get; set; }

    [JsonIgnore]
    private Workspace? Parent { get; set; }
    private Stopwatch Watch { get; set; } = new();

    public PosterReqRes() { }
    public PosterReqRes(Workspace workspace) { Parent = workspace; }

    public async Task SendAsync()
    {
        Error = null;
        long responseSize = 0;
        Watch.Reset();

        // Resolve variables in route and body
        var resolvedRoute = Resolve(Route);
        var resolvedBody = Resolve(Body);

        var uri = new Uri(resolvedRoute);
        RequestMessage = new HttpRequestMessage(HttpMethod, uri);

        // Attach body for methods that carry one
        if (HttpMethod != HttpMethod.Get &&
            HttpMethod != HttpMethod.Delete &&
            HttpMethod != HttpMethod.Head &&
            HttpMethod != HttpMethod.Options &&
            !string.IsNullOrEmpty(resolvedBody))
        {
            RequestMessage.Content = new StringContent(
                resolvedBody, System.Text.Encoding.UTF8, "application/json");
        }

        // Apply custom headers (variable-resolved values)
        foreach (var (key, value) in Headers)
            RequestMessage.Headers.TryAddWithoutValidation(key, Resolve(value));

        // Apply authentication
        ApplyAuth(RequestMessage);

        try
        {
            HttpClient client = Parent == null ? new HttpClient() : Parent.Client;
            Watch.Start();
            ResponseMessage = await client.SendAsync(RequestMessage);
            ResponseBody = await ResponseMessage.Content.ReadAsStringAsync();
            responseSize = System.Text.Encoding.UTF8.GetByteCount(ResponseBody);
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Watch.Stop();
            LastExec = DateTime.UtcNow;
        }

        Metric = Metrics.Update(
            System.Text.Encoding.UTF8.GetByteCount(resolvedBody),
            responseSize,
            Watch.ElapsedMilliseconds);
    }

    public void SetParent(Workspace workspace) => Parent = workspace;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string Resolve(string input) =>
        Parent?.Resolve(input) ?? input;

    private void ApplyAuth(HttpRequestMessage req)
    {
        switch (Auth.Type)
        {
            case AuthType.Bearer:
                if (!string.IsNullOrEmpty(Auth.Token))
                    req.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", Resolve(Auth.Token));
                break;

            case AuthType.Basic:
                {
                    var user = Resolve(Auth.Username);
                    var pass = Resolve(Auth.Password);
                    var encoded = Convert.ToBase64String(
                        System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}"));
                    req.Headers.Authorization =
                        new AuthenticationHeaderValue("Basic", encoded);
                    break;
                }

            case AuthType.ApiKey:
                if (!string.IsNullOrEmpty(Auth.Token))
                    req.Headers.TryAddWithoutValidation(
                        string.IsNullOrEmpty(Auth.ApiKeyHeader) ? "X-API-Key" : Auth.ApiKeyHeader,
                        Resolve(Auth.Token));
                break;
        }
    }
}
