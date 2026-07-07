using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public enum DebloatCategory { Telemetry, ScheduledTask, BloatwareApp }

public sealed record DebloatItem(DebloatCategory Category, string Key, string Name, string Description, bool IsClean)
{
    public string StatusLabel => IsClean ? "Nettoyé" : "Actif";
}

/// <summary>
/// Débloat Windows guidé : services et tâches planifiées de télémétrie,
/// applications préinstallées non essentielles. Chaque action est réversible
/// individuellement — sauf les applications, retirées uniquement pour
/// l'utilisateur courant et réinstallables depuis le Microsoft Store si la
/// restauration automatique échoue (Windows ne garantit pas la
/// réinstallation via Add-AppxPackage une fois le paquet purgé du cache).
/// L'app tourne déjà en administrateur (app.manifest), donc les écritures
/// HKLM et schtasks n'ont pas besoin d'élévation supplémentaire.
/// </summary>
public sealed class DebloatService
{
    private static readonly (string ServiceName, string DisplayName, string Description)[] TelemetryServices =
    [
        ("DiagTrack", "Connected User Experiences and Telemetry", "Collecte et envoie des données de diagnostic à Microsoft."),
        ("dmwappushservice", "WAP Push Message Routing Service", "Route les messages WAP Push (rarement utile hors contexte entreprise/mobile).")
    ];

    private static readonly (string TaskPath, string DisplayName)[] TelemetryTasks =
    [
        (@"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser", "Analyse de compatibilité des applications"),
        (@"\Microsoft\Windows\Application Experience\ProgramDataUpdater", "Mise à jour des données de compatibilité"),
        (@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", "Programme d'amélioration de l'expérience client"),
        (@"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip", "Télémétrie USB"),
        (@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", "Collecteur de diagnostic disque")
    ];

    private static readonly (string PackagePrefix, string DisplayName)[] BloatwareApps =
    [
        ("Microsoft.3DBuilder", "3D Builder"),
        ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection"),
        ("Microsoft.BingWeather", "Météo (Bing)"),
        ("Microsoft.BingNews", "Actualités (Bing)"),
        ("Microsoft.ZuneMusic", "Groove Musique"),
        ("Microsoft.ZuneVideo", "Films et TV"),
        ("Microsoft.People", "Contacts"),
        ("Microsoft.WindowsMaps", "Cartes"),
        ("Microsoft.GetHelp", "Obtenir de l'aide"),
        ("Microsoft.Getstarted", "Prise en main"),
        ("Microsoft.MixedReality.Portal", "Réalité mixte"),
        ("Microsoft.Print3D", "Print 3D"),
        ("king.com.CandyCrushSaga", "Candy Crush Saga")
    ];

    private const string ServicesRegistryRoot = @"SYSTEM\CurrentControlSet\Services\";
    private const int ServiceStartDisabled = 4;
    private const int ServiceStartManualDefault = 3;

    private readonly string _stateFile;
    private Dictionary<string, int> _previousServiceStart = new();

    public DebloatService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "debloat-state.json");
        LoadState();
    }

    public IReadOnlyList<DebloatItem> Scan()
    {
        var items = new List<DebloatItem>();

        foreach (var (serviceName, displayName, description) in TelemetryServices)
            items.Add(new DebloatItem(DebloatCategory.Telemetry, serviceName, displayName, description,
                IsClean: GetServiceStartType(serviceName) == ServiceStartDisabled));

        foreach (var (taskPath, displayName) in TelemetryTasks)
            items.Add(new DebloatItem(DebloatCategory.ScheduledTask, taskPath, displayName,
                "Tâche planifiée de télémétrie/diagnostic.", IsClean: !IsTaskEnabled(taskPath)));

        foreach (var (packagePrefix, displayName) in BloatwareApps)
            items.Add(new DebloatItem(DebloatCategory.BloatwareApp, packagePrefix, displayName,
                "Application préinstallée non essentielle.", IsClean: !IsAppInstalled(packagePrefix)));

        return items;
    }

    /// <summary>Applique le nettoyage pour une clé donnée ; l'état précédent est conservé pour Undo.</summary>
    public (bool Success, string Message) Clean(DebloatCategory category, string key)
    {
        try
        {
            switch (category)
            {
                case DebloatCategory.Telemetry:
                    _previousServiceStart[key] = GetServiceStartType(key);
                    SetServiceStartType(key, ServiceStartDisabled);
                    SaveState();
                    return (true, $"{key} désactivé.");

                case DebloatCategory.ScheduledTask:
                    RunCaptureOutput("schtasks.exe", $"/Change /TN \"{key}\" /Disable");
                    return (true, $"Tâche '{key}' désactivée.");

                case DebloatCategory.BloatwareApp:
                    var fullName = GetInstalledPackageFullName(key);
                    if (fullName is null)
                        return (true, "Déjà absent.");
                    RunCaptureOutput("powershell.exe",
                        $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{fullName}'\"");
                    return (true, $"'{key}' supprimé pour cet utilisateur.");

                default:
                    return (false, "Catégorie inconnue.");
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Débloat: nettoyage de '{key}' impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Restaure l'état précédent. Les applications se réinstallent depuis le Microsoft Store.</summary>
    public (bool Success, string Message) Undo(DebloatCategory category, string key)
    {
        try
        {
            switch (category)
            {
                case DebloatCategory.Telemetry:
                    var previous = _previousServiceStart.TryGetValue(key, out var v) ? v : ServiceStartManualDefault;
                    SetServiceStartType(key, previous);
                    _previousServiceStart.Remove(key);
                    SaveState();
                    return (true, $"{key} restauré.");

                case DebloatCategory.ScheduledTask:
                    RunCaptureOutput("schtasks.exe", $"/Change /TN \"{key}\" /Enable");
                    return (true, $"Tâche '{key}' réactivée.");

                case DebloatCategory.BloatwareApp:
                    return (false, "Réinstallez depuis le Microsoft Store si nécessaire.");

                default:
                    return (false, "Catégorie inconnue.");
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Débloat: restauration de '{key}' impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Nettoie tous les éléments non encore traités. Retourne le nombre réussi.</summary>
    public int CleanAll(IEnumerable<DebloatItem> items)
    {
        var count = 0;
        foreach (var item in items.Where(i => !i.IsClean))
        {
            var (success, _) = Clean(item.Category, item.Key);
            if (success) count++;
        }
        return count;
    }

    private static int GetServiceStartType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServicesRegistryRoot + serviceName, writable: false);
            return key?.GetValue("Start") is int v ? v : ServiceStartManualDefault;
        }
        catch
        {
            return ServiceStartManualDefault;
        }
    }

    private static void SetServiceStartType(string serviceName, int startType)
    {
        using var key = Registry.LocalMachine.OpenSubKey(ServicesRegistryRoot + serviceName, writable: true)
            ?? throw new InvalidOperationException($"Service '{serviceName}' introuvable ou droits insuffisants.");
        key.SetValue("Start", startType, RegistryValueKind.DWord);
    }

    private static bool IsTaskEnabled(string taskPath)
    {
        try
        {
            var output = RunCaptureOutput("schtasks.exe", $"/Query /TN \"{taskPath}\" /FO CSV /NH");
            return ParseTaskEnabledFromCsv(output);
        }
        catch
        {
            return false; // tâche absente/inaccessible : considérée comme neutralisée.
        }
    }

    internal static bool ParseTaskEnabledFromCsv(string csvLine)
    {
        csvLine = csvLine.Trim();
        if (csvLine.Length == 0)
            return false;

        var fields = csvLine.Split("\",\"").Select(f => f.Trim('"')).ToList();
        return !fields[^1].Equals("Disabled", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppInstalled(string packagePrefix) => GetInstalledPackageFullName(packagePrefix) is not null;

    private static string? GetInstalledPackageFullName(string packagePrefix)
    {
        var output = RunCaptureOutput("powershell.exe",
            $"-NoProfile -NonInteractive -Command \"(Get-AppxPackage -Name '{packagePrefix}*' | Select-Object -First 1).PackageFullName\"");
        var trimmed = output.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string RunCaptureOutput(string fileName, string arguments, int timeoutMs = 8000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de démarrer {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return output;
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
                _previousServiceStart = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_stateFile)) ?? new();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'état de débloat impossible", ex);
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_previousServiceStart));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde de l'état de débloat impossible", ex);
        }
    }
}
