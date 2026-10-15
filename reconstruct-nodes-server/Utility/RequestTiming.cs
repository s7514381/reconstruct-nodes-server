using System.Collections.Concurrent;
using System.Diagnostics;

namespace ReconstructNodesServer.Utility;

/// <summary>Opt-in aggregate timings only: never retains SQL, parameters, paths or player identity.</summary>
public sealed class RequestTiming : IDisposable
{
    private static readonly AsyncLocal<RequestTiming?> Ambient = new();
    private readonly RequestTiming? _previous;
    private readonly ConcurrentDictionary<string, (long Count, long Ticks)> _metrics = new();
    public static RequestTiming? Current => Ambient.Value;

    public RequestTiming()
    {
        _previous = Ambient.Value;
        Ambient.Value = this;
    }

    public static IDisposable? Measure(string category) => Current is { } timing
        ? new Measurement(timing, category) : null;

    public IReadOnlyDictionary<string, Metric> Snapshot() => _metrics.ToDictionary(
        item => item.Key, item => new Metric(item.Value.Count,
            item.Value.Ticks * 1000.0 / Stopwatch.Frequency));

    public sealed record Metric(long Count, double Milliseconds);

    private sealed class Measurement(RequestTiming owner, string category) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            long elapsed = Stopwatch.GetTimestamp() - _start;
            owner._metrics.AddOrUpdate(category, (1, elapsed),
                (_, old) => (old.Count + 1, old.Ticks + elapsed));
        }
    }

    public void Dispose() => Ambient.Value = _previous;
}
