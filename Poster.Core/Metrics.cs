namespace Poster.Core;

public struct Metrics
{
    public long RequestSize { get; init; }
    public long ResponseSize { get; init; }
    public long ElapsedTime { get; init; }

    public Metrics() { }

    public Metrics(long[] args)
    {
        if (args.Length is > 0 and <= 3)
        {
            RequestSize = args[0];
            ResponseSize = args[1];
            ElapsedTime = args[2];
        }
    }

    public static Metrics Update(long reqSize) =>
        new() { RequestSize = reqSize };

    public static Metrics Update(long reqSize, long resSize) =>
        new() { RequestSize = reqSize, ResponseSize = resSize };

    public static Metrics Update(long reqSize, long resSize, long elapsed) =>
        new() { RequestSize = reqSize, ResponseSize = resSize, ElapsedTime = elapsed };
}
