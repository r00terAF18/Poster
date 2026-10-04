namespace Poster.Core;

public struct Metrics
{
    public long RequestSize { get; init; }
    public long ResponseSize { get; init; }
    public long ElapsedTime { get; init; }

    public Metrics() { }

    public Metrics(long[]? metrics)
    {
        if (metrics != null && metrics.Length > 0)
        {
            RequestSize  = metrics.Length > 0 ? metrics[0] : 0;
            ResponseSize = metrics.Length > 1 ? metrics[1] : 0;
            ElapsedTime  = metrics.Length > 2 ? metrics[2] : 0;
        }
    }

    public static Metrics Update(long reqSize) =>
        new() { RequestSize = reqSize };

    public static Metrics Update(long reqSize, long resSize) =>
        new() { RequestSize = reqSize, ResponseSize = resSize };

    public static Metrics Update(long reqSize, long resSize, long elapsed) =>
        new() { RequestSize = reqSize, ResponseSize = resSize, ElapsedTime = elapsed };
}
