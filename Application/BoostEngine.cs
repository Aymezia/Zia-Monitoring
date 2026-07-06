using System.ServiceProcess;
using System.Text.Json;
using Microsoft.Win32;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class BoostEngine
{
    private static readonly string[] CandidateServices = ["SysMain", "WSearch", "DiagTrack"];
    private static readonly HashSet<string> ProtectedStartupNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SecurityHealth",
        "WindowsDefender"
    };

    private readonly string _boostRoot;
    private readonly string _rollbackRoot;

    public BoostEngine()
    {
        _boostRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZiaMonitoring", "Boost");
        _rollbackRoot = Path.Combine(_boostRoot, "rollback");

        Directory.CreateDirectory(_rollbackRoot);
    }

    public BoostPreviewResult BuildPreview()
    {
        var (tempFiles, tempBytes) = ScanTempCandidates(limit: 4000);
        var startupCandidates = ReadStartupCandidates();
        var serviceCandidates = ReadServiceCandidates();

        var planned = new List<string>
        {
            $"Cleanup temp files: {tempFiles} candidates ({tempBytes / 1024d / 1024d:F1} MB)",
            $"Startup optimization: {startupCandidates.Count} entry(ies)",
            $"Service optimization: {serviceCandidates.Count} service(s)"
        };

        return new BoostPreviewResult(
            GeneratedAt: DateTime.Now,
            TempFileCandidates: tempFiles,
            TempBytesCandidates: tempBytes,
            StartupCandidates: startupCandidates,
            ServiceCandidates: serviceCandidates,
            PlannedActions: planned);
    }

    public BoostExecutionResult ExecuteSafeBoost()
    {
        var warnings = new List<string>();
        var actions = new List<string>();
        var rollback = new BoostRollbackState
        {
            RollbackId = DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var deleted = CleanupUserTempFiles(warnings);
            actions.Add($"Temp cleanup executed: {deleted} file(s) removed.");

            var disabledStartup = DisableStartupEntries(warnings);
            rollback.DisabledStartupEntries = disabledStartup;
            actions.Add($"Startup optimization executed: {disabledStartup.Count} entry(ies) disabled.");

            var stoppedServices = StopCandidateServices(warnings);
            rollback.StoppedServices = stoppedServices;
            actions.Add($"Service optimization executed: {stoppedServices.Count} service(s) stopped.");

            SaveRollbackState(rollback);

            return new BoostExecutionResult(
                Success: true,
                RollbackId: rollback.RollbackId,
                AppliedActions: actions,
                Warnings: warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Boost failed: {ex.Message}");
            return new BoostExecutionResult(false, rollback.RollbackId, actions, warnings);
        }
    }

    public BoostRollbackResult RollbackLastBoost()
    {
        var warnings = new List<string>();
        var restored = new List<string>();

        var state = LoadLatestRollbackState();
        if (state is null)
        {
            return new BoostRollbackResult(false, string.Empty, restored, ["No rollback snapshot available."]);
        }

        try
        {
            var startupRestored = RestoreStartupEntries(state.DisabledStartupEntries, warnings);
            restored.Add($"Startup entries restored: {startupRestored}");

            var servicesRestarted = RestartServices(state.StoppedServices, warnings);
            restored.Add($"Services restarted: {servicesRestarted}");

            return new BoostRollbackResult(true, state.RollbackId, restored, warnings);
        }
        catch (Exception ex)
        {
            warnings.Add($"Rollback failed: {ex.Message}");
            return new BoostRollbackResult(false, state.RollbackId, restored, warnings);
        }
    }

    private static (int fileCount, long totalBytes) ScanTempCandidates(int limit)
    {
        var root = Path.GetTempPath();
        if (!Directory.Exists(root))
        {
            return (0, 0);
        }

        var count = 0;
        long bytes = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (count >= limit)
            {
                break;
            }

            try
            {
                var info = new FileInfo(file);
                bytes += info.Length;
                count++;
            }
            catch
            {
                // Skip inaccessible files.
            }
        }

        return (count, bytes);
    }

    private static int CleanupUserTempFiles(List<string> warnings)
    {
        var root = Path.GetTempPath();
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var deleted = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(3000))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc > DateTime.UtcNow.AddHours(-4))
                {
                    continue;
                }

                info.Delete();
                deleted++;
            }
            catch
            {
                // Locked or protected files are expected.
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir, false);
                }
            }
            catch
            {
                // Ignore directory cleanup failures.
            }
        }

        if (deleted == 0)
        {
            warnings.Add("Temp cleanup removed no file. Most files may be locked or already clean.");
        }

        return deleted;
    }

    private static List<string> ReadStartupCandidates()
    {
        var result = new List<string>();

        using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
        if (runKey is null)
        {
            return result;
        }

        foreach (var name in runKey.GetValueNames())
        {
            if (ProtectedStartupNames.Contains(name))
            {
                continue;
            }

            result.Add(name);
        }

        return result;
    }

    private static Dictionary<string, string> DisableStartupEntries(List<string> warnings)
    {
        var disabled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (runKey is null)
        {
            warnings.Add("Startup registry key not available.");
            return disabled;
        }

        foreach (var name in runKey.GetValueNames())
        {
            if (ProtectedStartupNames.Contains(name))
            {
                continue;
            }

            var value = runKey.GetValue(name)?.ToString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            disabled[name] = value;
            runKey.DeleteValue(name, throwOnMissingValue: false);
        }

        return disabled;
    }

    private static int RestoreStartupEntries(Dictionary<string, string> startupEntries, List<string> warnings)
    {
        if (startupEntries.Count == 0)
        {
            return 0;
        }

        using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (runKey is null)
        {
            warnings.Add("Cannot restore startup entries: Run registry key unavailable.");
            return 0;
        }

        var restored = 0;
        foreach (var item in startupEntries)
        {
            try
            {
                runKey.SetValue(item.Key, item.Value, RegistryValueKind.String);
                restored++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Startup restore failed for {item.Key}: {ex.Message}");
            }
        }

        return restored;
    }

    private static List<string> ReadServiceCandidates()
    {
        var running = new List<string>();

        try
        {
            var services = ServiceController.GetServices();
            foreach (var name in CandidateServices)
            {
                var service = services.FirstOrDefault(s => s.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (service is not null && service.Status == ServiceControllerStatus.Running)
                {
                    running.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            // Non-admin contexts can fail to query services.
            Infrastructure.AppLog.Warn("Enumeration des services impossible (droits insuffisants ?)", ex);
        }

        return running;
    }

    private static List<string> StopCandidateServices(List<string> warnings)
    {
        var stopped = new List<string>();

        foreach (var serviceName in CandidateServices)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                if (sc.Status == ServiceControllerStatus.Running && sc.CanStop)
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(4));
                    stopped.Add(serviceName);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Service action skipped for {serviceName}: {ex.Message}");
            }
        }

        return stopped;
    }

    private static int RestartServices(IEnumerable<string> services, List<string> warnings)
    {
        var restarted = 0;

        foreach (var name in services)
        {
            try
            {
                using var sc = new ServiceController(name);
                if (sc.Status != ServiceControllerStatus.Running)
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(5));
                }

                restarted++;
            }
            catch (Exception ex)
            {
                warnings.Add($"Service restart failed for {name}: {ex.Message}");
            }
        }

        return restarted;
    }

    private void SaveRollbackState(BoostRollbackState state)
    {
        var file = Path.Combine(_rollbackRoot, $"rollback-{state.RollbackId}.json");
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(file, json);
    }

    private BoostRollbackState? LoadLatestRollbackState()
    {
        var latest = Directory
            .EnumerateFiles(_rollbackRoot, "rollback-*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latest))
        {
            return null;
        }

        var json = File.ReadAllText(latest);
        return JsonSerializer.Deserialize<BoostRollbackState>(json);
    }
}
