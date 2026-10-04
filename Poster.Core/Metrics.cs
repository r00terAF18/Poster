using System;

namespace Poster.Core;

public struct Metrics
{
    long request_size = 0;
    long response_size = 0;
    long elapsed_time = 0;

    public long RequestSize { get; init; }
    public long ResponseSize { get; init; }
    public long ElapsedTime { get; init; }


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