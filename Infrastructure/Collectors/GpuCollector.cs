using System.Management;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class GpuCollector
{
    public (double? TemperatureC, double? UsagePercent) GetGpuStats()
    {
        double? usage = null;

        // WMI GPU performance counters for usage % (available on modern Windows)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT Name, UtilizationDefault FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            double maxUsage = 0;
            foreach (ManagementObject item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(item["UtilizationDefault"]?.ToString(), out var u) && u > maxUsage)
                        maxUsage = u;
                }
            }
            if (maxUsage > 0) usage = maxUsage;
        }
        catch { }

        // GPU temperature: delegate to TemperatureCollector ACPI zones
        var gpuTemp = TemperatureCollector.GetGpuTemperatureC();

        return (gpuTemp, usage);
    }
}
