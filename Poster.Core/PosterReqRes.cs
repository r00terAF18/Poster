using System;
using System.Data;
using System.Diagnostics;

namespace Poster.Core;

public struct Metrics
{
    long request_size = 0;
    long response_size = 0;
    long elapsed_time = 0;

    public Metrics()
    {
    }

    public Metrics(long[] args)
    {
        if (args.Length is > 0 and <= 4)
        {
            request_size = args[0];
            response_size = args[1];
            elapsed_time = args[2];
        }
    }
    public static Metrics Update(long req_size)
    {
        return new Metrics() { request_size = req_size };
    }

    public static Metrics Update(long req_size, long res_size)
    {
        return new Metrics() { request_size = req_size, response_size = res_size };
    }

    public static Metrics Update(long req_size, long res_size, long elapsed)
    {
        return new Metrics() { request_size = req_size, response_size = res_size, elapsed_time = elapsed };
    }
}

public class PosterReqRes
{
    public required HttpMethod HttpMethod { get; set; } = HttpMethod.Get;
    public required string Route { get; set; }

    public required HttpRequestMessage RequestMessage { get; set; }
    public required HttpResponseMessage ResponseMessage { get; set; }
    public string Body { get; set; } = "";
    public string? Error { get; set; }

    public DateTime LastExec { get; set; }

    private Workspace Parent { get; set; }
    public Metrics Metric { get; set; }

    public PosterReqRes(Workspace workspace)
    {
        Parent = workspace;
    }

    public async Task SendAsync()
    {
        Error = null;
        
        var uri = new Uri(Route);
        Stopwatch watch = new();
        RequestMessage = new HttpRequestMessage(HttpMethod, uri);
        if (HttpMethod != HttpMethod.Get)
        {
            var body = new StringContent(Body, System.Text.Encoding.UTF8, "application/json");
            RequestMessage.Content = body;
        }

        try
        {
            watch.Start();
            ResponseMessage = await Parent.Client.SendAsync(RequestMessage);
            watch.Stop();
        }
        catch (HttpRequestException ex)
        {
            Error = ex.Message;
        }

        Metric = Metrics.Update(System.Text.Encoding.UTF8.GetByteCount(Body), 0, watch.ElapsedMilliseconds);
    }
}
