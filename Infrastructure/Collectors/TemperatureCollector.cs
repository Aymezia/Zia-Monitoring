using System.Management;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class TemperatureCollector
{
    public double? GetCpuTemperatureC()
    {
        // Primary: ACPI thermal zones (works without admin on most modern Windows 10/11)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            double? best = null;
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (obj["CurrentTemperature"] is uint raw)
                {
                    var celsius = (raw - 2732) / 10.0;
                    // Typical CPU temp range: 20-105 C
                    if (celsius is >= 20 and <= 105)
                    {
                        if (best is null || celsius > best)
                            best = celsius;
                    }
                }
            }

            if (best.HasValue)
                return Math.Round(best.Value, 1);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Zones thermiques ACPI indisponibles (CPU)", ex);
        }

        // Fallback: Win32_Processor (rarely populated but try anyway)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CurrentTemperature FROM Win32_Processor");
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (obj["CurrentTemperature"] is uint raw && raw > 0)
                {
                    var celsius = raw - 273.15;
                    if (celsius is >= 20 and <= 105)
                        return Math.Round(celsius, 1);
                }
            }
        }
        catch { }

        return null;
    }

    public static double? GetGpuTemperatureC()
    {
        // GPU thermal zone via ACPI (discrete GPUs expose a separate zone)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");

            double? gpuTemp = null;
            double? maxTemp = null;

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (obj["CurrentTemperature"] is not uint raw)
                    continue;

                var celsius = (raw - 2732) / 10.0;
                if (celsius is < 20 or > 105)
                    continue;

                var instance = obj["InstanceName"]?.ToString() ?? string.Empty;

                // GPU thermal zones are often labeled TZ01, TZ1, or contain GPU/VGA
                if (instance.Contains("TZ01", StringComparison.OrdinalIgnoreCase)
                    || instance.Contains("GPU", StringComparison.OrdinalIgnoreCase)
                    || instance.Contains("VGA", StringComparison.OrdinalIgnoreCase))
                {
                    if (gpuTemp is null || celsius > gpuTemp)
                        gpuTemp = celsius;
                }

                if (maxTemp is null || celsius > maxTemp)
                    maxTemp = celsius;
            }

            // Return explicit GPU zone if found, otherwise null (not the highest overall)
            if (gpuTemp.HasValue)
                return Math.Round(gpuTemp.Value, 1);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Zones thermiques ACPI indisponibles (GPU)", ex);
        }

        return null;
    }
}
