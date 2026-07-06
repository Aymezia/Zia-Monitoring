using System.Diagnostics;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class EstimatedFpsCollector : IDisposable
{
    private static readonly string[] CounterCandidates =
    [
        "Presented Frames/sec",
        "Presentation Frames/sec",
        "Frames Presented/sec",
        "Frame Rate"
    ];

    private readonly List<PerformanceCounter> _counters = new();
    private bool _initialized;
    private bool _disposed;

    public double GetEstimatedFps()
    {
        if (_disposed)
            return 0;

        EnsureCounters();
        if (_counters.Count == 0)
            return 0;

        double total = 0;
        foreach (var counter in _counters)
        {
            try
            {
                total += Math.Max(0, counter.NextValue());
            }
            catch
            {
                // GPU Engine counters can disappear when a game exits.
            }
        }

        return Math.Round(total, 0);
    }

    private void EnsureCounters()
    {
        if (_initialized)
            return;

        _initialized = true;

        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
                return;

            var category = new PerformanceCounterCategory("GPU Engine");
            foreach (var instance in category.GetInstanceNames())
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)
                    && !instance.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var counters = category.GetCounters(instance)
                    .Select(counter => counter.CounterName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var candidate in CounterCandidates)
                {
                    if (!counters.Contains(candidate))
                        continue;

                    var counter = new PerformanceCounter("GPU Engine", candidate, instance, readOnly: true);
                    _ = counter.NextValue();
                    _counters.Add(counter);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Initialisation des compteurs GPU Engine impossible", ex);
            DisposeCounters();
        }
    }

    private void DisposeCounters()
    {
        foreach (var counter in _counters)
        {
            try { counter.Dispose(); } catch { }
        }

        _counters.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        DisposeCounters();
    }
}
