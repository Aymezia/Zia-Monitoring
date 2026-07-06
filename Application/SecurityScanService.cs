using System.Management;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
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
        var maliciousProcesses = ScanProcessSignatures();
        var keyloggerWarnings = ScanKeyboardHookIndicators();
        var riskScore = ComputeRiskScore(firewall, uac, suspicious, drivers, smart, maliciousProcesses, keyloggerWarnings);

        return new SecurityReport(
            DateTime.Now,
            riskScore,
            firewall,
            uac,
            ports,
            suspicious,
            drivers,
            smart,
            maliciousProcesses,
            keyloggerWarnings);
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

    private static IReadOnlyList<string> ScanProcessSignatures()
    {
        var result = new List<string>();
        var signatures = LoadKnownSha256Signatures();

        foreach (var process in System.Diagnostics.Process.GetProcesses().Take(180))
        {
            using (process)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                        continue;

                    using var stream = File.OpenRead(path);
                    var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    if (signatures.Contains(hash))
                        result.Add($"{process.ProcessName} ({process.Id}) - {hash}");
                }
                catch
                {
                    // Access denied is expected for protected processes.
                }
            }
        }

        return result;
    }

    private static HashSet<string> LoadKnownSha256Signatures()
    {
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "275a021bbfb6489e54d471899f7db9d1663fc695ec2fe2a2c4538aabf651fd0f"
        };

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZiaMonitoring",
                "known-malware-sha256.txt");

            if (!File.Exists(path))
                return signatures;

            foreach (var line in File.ReadLines(path))
            {
                var value = line.Trim();
                if (value.Length == 64 && value.All(Uri.IsHexDigit))
                    signatures.Add(value);
            }
        }
        catch { }

        return signatures;
    }

    private static IReadOnlyList<string> ScanKeyboardHookIndicators()
    {
        var result = new List<string>();
        var suspiciousTokens = new[]
        {
            "keylogger", "keyboardhook", "keyhook", "inputhook", "interception", "globalhook"
        };

        foreach (var process in System.Diagnostics.Process.GetProcesses().Take(180))
        {
            using (process)
            {
                try
                {
                    var name = process.ProcessName;
                    if (suspiciousTokens.Any(token => name.Contains(token, StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Add($"{name} ({process.Id}) - nom de processus suspect");
                        continue;
                    }

                    foreach (System.Diagnostics.ProcessModule module in process.Modules)
                    {
                        var moduleName = module.ModuleName ?? string.Empty;
                        if (suspiciousTokens.Any(token => moduleName.Contains(token, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add($"{name} ({process.Id}) - module hook probable: {moduleName}");
                            break;
                        }
                    }
                }
                catch
                {
                    // Module enumeration is restricted for many system processes.
                }
            }
        }

        return result;
    }

    private static int ComputeRiskScore(
        bool firewall,
        bool uac,
        IReadOnlyList<string> suspicious,
        IReadOnlyList<string> drivers,
        IReadOnlyList<string> smart,
        IReadOnlyList<string> maliciousProcesses,
        IReadOnlyList<string> keyloggerWarnings)
    {
        var score = 0;
        if (!firewall) score += 18;
        if (!uac) score += 22;
        score += Math.Min(20, suspicious.Count * 8);
        score += Math.Min(14, drivers.Count);
        score += Math.Min(20, smart.Count * 12);
        score += Math.Min(45, maliciousProcesses.Count * 30);
        score += Math.Min(35, keyloggerWarnings.Count * 18);
        return Math.Clamp(score, 0, 100);
    }
}
