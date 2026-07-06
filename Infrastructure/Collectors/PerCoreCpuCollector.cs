using System.Diagnostics;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class PerCoreCpuCollector
{
    private readonly List<PerformanceCounter?> _counters = new();
    private bool _initialized;

    public IReadOnlyList<PerCoreCpuUsage> GetPerCoreUsage()
    {
        try
        {
            if (!_initialized)
            {
                _initialized = true;
                for (var i = 0; i < Environment.ProcessorCount; i++)
                {
                    try
                    {
                        _counters.Add(new PerformanceCounter("Processor", "% Processor Time", i.ToString()));
                    }
                    catch
                    {
                        _counters.Add(null);
                    }
                }
            }

            var results = new List<PerCoreCpuUsage>();
            for (var i = 0; i < _counters.Count; i++)
            {
                double pct = 0;
                try
                {
                    pct = _counters[i]?.NextValue() ?? 0;
                }
                catch { }

                results.Add(new PerCoreCpuUsage(i, Math.Clamp(pct, 0, 100)));
            }
            return results;
        }
        catch
        {
            return Array.Empty<PerCoreCpuUsage>();
        }
    }
}
