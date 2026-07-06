using System.Management;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class SecurityScanService
{
    public SecurityReport BuildReport()
    {
        var firewall = IsFirewallEnabled();
        var uac = IsUacEnabled();
        var ports = ScanOpenPorts();
        var suspicious = ScanSuspiciousStartup();
        var drivers = ScanObsoleteDrivers();
        var smart = ScanSmartWarnings();

        return new SecurityReport(firewall, uac, ports, suspicious, drivers, smart);
    }

    private static bool IsFirewallEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", false);
            return key?.GetValue("EnableFirewall") is int v && v == 1;
        }
        catch { return false; }
    }

    private static bool IsUacEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", false);
            return key?.GetValue("EnableLUA") is int v && v == 1;
        }
        catch { return false; }
    }

    private static IReadOnlyList<string> ScanOpenPorts()
    {
        var result = new List<string>();
        try
        {
            var props = IPGlobalProperties.GetIPGlobalProperties();
            foreach (var ep in props.GetActiveTcpListeners().Take(30))
                result.Add($"TCP :{ep.Port}");
        }
        catch { }
        return result;
    }

    private static readonly HashSet<string> KnownSuspiciousPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "cryptominer", "xmrig", "coinhive", "darkcomet", "njrat", "keylogger",
        "trojan", "backdoor", "remcos", "asyncrat"
    };

    private static IReadOnlyList<string> ScanSuspiciousStartup()
    {
        var result = new List<string>();

        void ScanKey(RegistryKey? key)
        {
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                var value = key.GetValue(name)?.ToString() ?? string.Empty;
                var lower = (name + value).ToLowerInvariant();
                if (KnownSuspiciousPatterns.Any(p => lower.Contains(p)))
                    result.Add($"{name}: {value}");
            }
        }

        try
        {
            ScanKey(Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"));
            ScanKey(Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"));
        }
        catch { }

        return result;
    }

    private static IReadOnlyList<string> ScanObsoleteDrivers()
    {
        var result = new List<string>();
        try
        {
            var threshold = DateTime.Now.AddYears(-3);
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverDate FROM Win32_PnPSignedDriver WHERE DriverDate IS NOT NULL");

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var name = obj["Name"]?.ToString();
                var dateStr = obj["DriverDate"]?.ToString();
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dateStr))
                    continue;

                // WMI date format: yyyyMMddHHmmss.xxxxxx+000
                if (dateStr.Length >= 8
                    && int.TryParse(dateStr[..4], out var year)
                    && int.TryParse(dateStr[4..6], out var month)
                    && int.TryParse(dateStr[6..8], out var day))
                {
                    var driverDate = new DateTime(year, Math.Clamp(month, 1, 12), Math.Clamp(day, 1, 28));
                    if (driverDate < threshold)
                        result.Add($"{name} ({driverDate:yyyy-MM-dd})");
                }

                if (result.Count >= 20) break;
            }
        }
        catch { }
        return result;
    }

    private static IReadOnlyList<string> ScanSmartWarnings()
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                if (obj["PredictFailure"] is bool fail && fail)
                {
                    var name = obj["InstanceName"]?.ToString() ?? "Disque inconnu";
                    result.Add($"SMART: defaillance prevue sur {name}");
                }
            }
        }
        catch { }
        return result;
    }
}
