using System.Management;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class FanAndVramCollector
{
    public double? GetFanSpeedRpm()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2", "SELECT DesiredSpeed FROM Win32_Fan");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (obj["DesiredSpeed"] is uint rpm && rpm > 0)
                    return rpm;
            }
        }
        catch { }

        // Fallback: ACPI fan via wmi
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT Active FROM MSAcpi_ThermalZoneTemperature");
        }
        catch { }

        return null;
    }

    public (double UsedMb, double TotalMb) GetVramUsage()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT AdapterRAM, CurrentRefreshRate FROM Win32_VideoController");

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var totalBytes = obj["AdapterRAM"] switch
                {
                    uint u => (long)u,
                    ulong ul => (long)ul,
                    _ => 0L
                };

                if (totalBytes <= 0)
                    continue;

                var totalMb = totalBytes / 1024.0 / 1024.0;

                // Estimate used VRAM via GPU perf counter (AdapterMemoryUsed)
                double usedMb = 0;
                try
                {
                    using var memSearcher = new ManagementObjectSearcher(
                        @"root\cimv2",
                        "SELECT Name, UtilizationDefault FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
                    foreach (var memObj in memSearcher.Get().Cast<ManagementObject>())
                    {
                        if (memObj["UtilizationDefault"] is ulong pct)
                            usedMb = totalMb * pct / 100.0;
                    }
                }
                catch { }

                return (Math.Round(usedMb, 1), Math.Round(totalMb, 1));
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lecture VRAM via WMI impossible", ex);
        }

        return (0, 0);
    }
}
