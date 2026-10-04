using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Poster.Core;

public class PosterReqRes
{
    public required HttpMethod HttpMethod { get; set; } = HttpMethod.Get;
    public required string Route { get; set; }
    public string Name { get; set; } = "New Request";

    [JsonIgnore]
    public HttpRequestMessage RequestMessage { get; set; }
    [JsonIgnore]
    public HttpResponseMessage ResponseMessage { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();

    public string? ResponseBody { get; private set; }

    public string Body { get; set; } = "";
    public string? Error { get; set; }

    public DateTime LastExec { get; set; }

    /// <summary>
    /// Either a request belongs to a workspace, or is just a one off standalone.
    /// </summary>
    [JsonIgnore]
    private Workspace? Parent { get; set; }
    public Metrics Metric { get; set; }
    private Stopwatch Watch { get; set; } = new();

    public PosterReqRes()
    {

    }

    public PosterReqRes(Workspace workspace)
    {
        Parent = workspace;
    }

    public async Task SendAsync()
    {
        Error = null;
        long responseSize = 0;
        Watch.Reset();

        var uri = new Uri(Route);
        RequestMessage = new HttpRequestMessage(HttpMethod, uri);
        if (HttpMethod != HttpMethod.Get)
        {
            var body = new StringContent(Body, System.Text.Encoding.UTF8, "application/json");
            RequestMessage.Content = body;
        }

        foreach (var (key, value) in Headers)
        {
            RequestMessage.Headers.TryAddWithoutValidation(key, value);
        }



        try
        {
            HttpClient client = Parent == null ? new() : Parent.Client;
            Watch.Start();
            ResponseMessage = await client.SendAsync(RequestMessage);
            var responseBody = await ResponseMessage.Content.ReadAsStringAsync();
            ResponseBody = responseBody;
            responseSize = System.Text.Encoding.UTF8.GetByteCount(responseBody);
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Watch.Stop();
        }

        Metric = Metrics.Update(System.Text.Encoding.UTF8.GetByteCount(Body), responseSize, Watch.ElapsedMilliseconds);
    }


    public void SetParent(Workspace workspace) => Parent = workspace;

}
