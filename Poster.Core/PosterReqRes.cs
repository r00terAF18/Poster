using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Text;

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

    /// <summary>Query parameters. Supports {{variable}} interpolation in values.</summary>
    public Dictionary<string, string> QueryParams { get; set; } = new();

    /// <summary>Authentication configuration for this request.</summary>
    public AuthConfig Auth { get; set; } = new();

    public string? ResponseBody { get; private set; }
    public string Body { get; set; } = "";
    public string? Error { get; set; }
    public DateTime LastExec { get; set; }
    public Metrics Metric { get; set; }

    [JsonIgnore]
    private Workspace? Parent { get; set; }
    private readonly Stopwatch Watch = new();
    private static readonly HttpClient StandaloneClient = new();

    public PosterReqRes() { }
    public PosterReqRes(Workspace workspace) { Parent = workspace; }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        Error = null;
        long responseSize = 0;
        Watch.Reset();

        RequestMessage?.Dispose();
        ResponseMessage?.Dispose();
        RequestMessage = null;
        ResponseMessage = null;
        ResponseBody = null;

        // Resolve variables in route and body
        var resolvedRoute = Resolve(Route);
        var resolvedBody = Resolve(Body);

        var uri = BuildUri(resolvedRoute);
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
            HttpClient client = Parent?.Client ?? StandaloneClient;
            Watch.Start();
            ResponseMessage = await client.SendAsync(
                RequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            ResponseBody = await ResponseMessage.Content.ReadAsStringAsync(cancellationToken);
            responseSize = System.Text.Encoding.UTF8.GetByteCount(ResponseBody);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Error = "Request cancelled.";
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

    private Uri BuildUri(string route)
    {
        var builder = new UriBuilder(route);
        if (QueryParams.Count == 0)
            return builder.Uri;

        var query = new StringBuilder(builder.Query.TrimStart('?'));
        foreach (var (key, value) in QueryParams)
        {
            if (query.Length > 0) query.Append('&');
            query.Append(Uri.EscapeDataString(Resolve(key)))
                .Append('=')
                .Append(Uri.EscapeDataString(Resolve(value)));
        }

        builder.Query = query.ToString();
        return builder.Uri;
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
