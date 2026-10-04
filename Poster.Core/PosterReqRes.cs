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

public class FileUpload
{
    /// <summary>Path to the file that will be included in a multipart request.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Name of the multipart form field that receives the file.</summary>
    public string FieldName { get; set; } = "file";
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

    /// <summary>Files to send as multipart form-data.</summary>
    public List<FileUpload> Files { get; set; } = [];

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
        long requestSize = 0;
        long responseSize = 0;
        Watch.Reset();

        RequestMessage?.Dispose();
        ResponseMessage?.Dispose();
        RequestMessage = null;
        ResponseMessage = null;
        ResponseBody = null;

        try
        {
            // Resolve variables in route and body
            string resolvedRoute = Resolve(Route);
            string resolvedBody = Resolve(Body);

            RequestMessage = new HttpRequestMessage(HttpMethod, BuildUri(resolvedRoute))
            {
                Content = BuildContent(resolvedBody),
            };

            // Add custom headers (skip Content-Type here because it is set on req.Content)
            foreach ((string key, string value) in Headers)
            {
                if (RequestMessage.Content != null &&
                    string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                RequestMessage.Headers.TryAddWithoutValidation(Resolve(key), Resolve(value));
            }

            // Apply authentication
            ApplyAuth(RequestMessage);

            requestSize = RequestMessage.Content?.Headers.ContentLength ?? 0;

            HttpClient client = Parent?.Client ?? StandaloneClient;
            Watch.Start();
            ResponseMessage = await client.SendAsync(
                RequestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            ResponseBody = await ResponseMessage.Content.ReadAsStringAsync(cancellationToken);
            responseSize = Encoding.UTF8.GetByteCount(ResponseBody);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Error = "Request cancelled.";
        }
        catch (OperationCanceledException)
        {
            Error = "Request timed out.";
        }
        catch (Exception ex)
        {
            // Bad URL, missing upload file, network failure, etc. - report instead of crashing the UI.
            Error = ex.Message;
        }
        finally
        {
            Watch.Stop();
            LastExec = DateTime.UtcNow;

            // Release upload file handles as soon as the exchange is over.
            RequestMessage?.Content?.Dispose();
        }

        Metric = Metrics.Update(requestSize, responseSize, Watch.ElapsedMilliseconds);
    }

    /// <summary>Builds the request content (multipart, string or none). Never leaks file handles on failure.</summary>
    private HttpContent? BuildContent(string resolvedBody)
    {
        if (HttpMethod == HttpMethod.Get ||
            HttpMethod == HttpMethod.Delete ||
            HttpMethod == HttpMethod.Head ||
            HttpMethod == HttpMethod.Options)
            return null;

        if (Files.Count > 0)
        {
            MultipartFormDataContent multipart = new();
            try
            {
                foreach (FileUpload file in Files)
                {
                    string filePath = Resolve(file.FilePath);
                    string fieldName = Resolve(file.FieldName);
                    FileStream stream = File.OpenRead(filePath);
                    try
                    {
                        multipart.Add(
                            new StreamContent(stream),
                            string.IsNullOrWhiteSpace(fieldName) ? "file" : fieldName,
                            Path.GetFileName(filePath));
                    }
                    catch
                    {
                        stream.Dispose();
                        throw;
                    }
                }
            }
            catch
            {
                multipart.Dispose(); // also disposes streams already added
                throw;
            }

            return multipart;
        }

        if (string.IsNullOrEmpty(resolvedBody))
            return null;

        // Check if user specified a custom Content-Type header.
        string? contentTypeKey = Headers.Keys.FirstOrDefault(k =>
            string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase));

        string mediaType = contentTypeKey != null && !string.IsNullOrWhiteSpace(Headers[contentTypeKey])
            ? Resolve(Headers[contentTypeKey])
            : "application/json";

        return MediaTypeHeaderValue.TryParse(mediaType, out MediaTypeHeaderValue? parsedMediaType)
            ? new StringContent(resolvedBody, Encoding.UTF8, parsedMediaType)
            : new StringContent(resolvedBody, Encoding.UTF8, "application/json");
    }

    private Uri BuildUri(string route)
    {
        UriBuilder builder = new UriBuilder(route);
        if (QueryParams.Count == 0)
            return builder.Uri;

        StringBuilder query = new StringBuilder(builder.Query.TrimStart('?'));
        foreach ((string key, string value) in QueryParams)
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
                    string user = Resolve(Auth.Username);
                    string pass = Resolve(Auth.Password);
                    string encoded = Convert.ToBase64String(
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
