using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Http;

namespace HappyHeadlines.Shared.Observability;

/// <summary>
/// Counts cache lookups as hits and misses so the cache hit ratio can be shown on the Grafana dashboard.
/// Exported as the Prometheus metric cache_requests_total{cache, operation, result}.
/// </summary>
public static class CacheMetrics
{
    public const string MeterName = "HappyHeadlines.Cache";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Requests = Meter.CreateCounter<long>(
        "cache.requests", description: "Cache lookups, tagged with the cache, the operation and hit or miss.");

    /// <summary>Records the lookup and tells the client through the X-Cache header.</summary>
    public static void Record(HttpResponse response, string cache, string operation, bool hit)
    {
        var result = hit ? "hit" : "miss";
        response.Headers["X-Cache"] = hit ? "HIT" : "MISS";
        Requests.Add(1,
            new KeyValuePair<string, object?>("cache", cache),
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("result", result));
    }
}
