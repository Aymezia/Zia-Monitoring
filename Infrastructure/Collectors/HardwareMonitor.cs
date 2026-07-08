using LibreHardwareMonitor.Hardware;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

/// <summary>
/// Shared LibreHardwareMonitor computer instance. Opt-in uniquement : l'ouverture
/// installe un driver kernel (WinRing0) que le blocklist Microsoft des drivers
/// vulnérables fait supprimer par Defender (CVE connues d'accès mémoire/MSR
/// arbitraire, exploitées dans des attaques BYOVD). Ne jamais l'ouvrir sans
/// consentement explicite de l'utilisateur (voir AppSettings.EnableHardwareSensors).
/// Must be disposed when the application exits.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    private Computer? _computer;
    private bool _disposed;
    private bool _opened;

    public void EnableSensors()
    {
        if (_opened || _disposed)
        {
            return;
        }

        _opened = true;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsMemoryEnabled = false
        };

        try
        {
            _computer.Open();
        }
        catch (Exception ex)
        {
            // Falls back to N/A gracefully if access denied.
            AppLog.Warn("LibreHardwareMonitor indisponible: temperatures/ventilateurs en N/A", ex);
        }
    }

    public HardwareReadings Read()
    {
        if (_computer is null)
        {
            return new HardwareReadings(null, null, null, null, null, null);
        }

        double? cpuTemp = null;
        double? gpuTemp = null;
        double? gpuUsage = null;
        double? fanRpm = null;
        double? cpuClock = null;
        double? gpuClock = null;

        try
        {
            foreach (var hw in _computer.Hardware)
            {
                hw.Update();

                switch (hw.HardwareType)
                {
                    case HardwareType.Cpu:
                        cpuTemp = ExtractTemp(hw, "CPU Package")
                                  ?? ExtractTemp(hw, "Core Average")
                                  ?? ExtractMaxTemp(hw);
                        fanRpm ??= ExtractFan(hw);
                        cpuClock = ExtractAverageCoreClock(hw);
                        break;

                    case HardwareType.GpuNvidia:
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuIntel:
                        gpuTemp = ExtractTemp(hw, "GPU Core")
                                  ?? ExtractMaxTemp(hw);
                        gpuUsage = ExtractLoad(hw, "GPU Core");
                        gpuClock = ExtractClock(hw, "GPU Core");
                        fanRpm ??= ExtractFan(hw);
                        break;

                    case HardwareType.Motherboard:
                        // sub-hardware on some boards
                        foreach (var sub in hw.SubHardware)
                        {
                            sub.Update();
                            fanRpm ??= ExtractFan(sub);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lecture des capteurs LibreHardwareMonitor en echec", ex);
        }

        return new HardwareReadings(cpuTemp, gpuTemp, gpuUsage, fanRpm, cpuClock, gpuClock);
    }

    /// <summary>Moyenne des horloges « Core #n » (exclut le bus).</summary>
    private static double? ExtractAverageCoreClock(IHardware hw)
    {
        double sum = 0;
        var count = 0;
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Clock
                && s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase)
                && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase)
                && s.Value is > 0)
            {
                sum += s.Value.Value;
                count++;
            }
        }
        return count > 0 ? Math.Round(sum / count, 0) : null;
    }

    private static double? ExtractClock(IHardware hw, string nameHint)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Clock
                && s.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase)
                && s.Value is > 0)
            {
                return Math.Round(s.Value.Value, 0);
            }
        }
        return null;
    }

    private static double? ExtractTemp(IHardware hw, string nameHint)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Temperature
                && s.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase)
                && s.Value.HasValue
                && s.Value > 0)
            {
                return Math.Round(s.Value.Value, 1);
            }
        }
        return null;
    }

    private static double? ExtractMaxTemp(IHardware hw)
    {
        double? max = null;
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Temperature && s.Value > 0)
            {
                if (max is null || s.Value > max)
                    max = Math.Round(s.Value.Value, 1);
            }
        }
        return max;
    }

    private static double? ExtractLoad(IHardware hw, string nameHint)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Load
                && s.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase)
                && s.Value.HasValue)
            {
                return Math.Round(s.Value.Value, 1);
            }
        }
        return null;
    }

    private static double? ExtractFan(IHardware hw)
    {
        foreach (var s in hw.Sensors)
        {
            if (s.SensorType == SensorType.Fan && s.Value > 0)
                return Math.Round(s.Value.Value, 0);
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _computer?.Close(); } catch { }
    }
}

public sealed record HardwareReadings(
    double? CpuTemperatureC,
    double? GpuTemperatureC,
    double? GpuUsagePercent,
    double? FanSpeedRpm,
    double? CpuClockMhz = null,
    double? GpuClockMhz = null);
