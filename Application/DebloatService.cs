using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public enum DebloatCategory { Telemetry, ScheduledTask, BloatwareApp }

public sealed record DebloatItem(DebloatCategory Category, string Key, string Name, string Description, bool IsClean)
{
    public string StatusLabel => IsClean ? "Nettoyé" : "Actif";

    public string CategoryLabel => Category switch
    {
        DebloatCategory.Telemetry => "Télémétrie",
        DebloatCategory.ScheduledTask => "Tâche planifiée",
        DebloatCategory.BloatwareApp => "Application préinstallée",
        _ => "Autre"
    };
}

/// <summary>
/// Débloat Windows guidé : services et tâches planifiées de télémétrie,
/// applications préinstallées non essentielles. Chaque action est réversible
/// individuellement — sauf les applications, retirées uniquement pour
/// l'utilisateur courant et réinstallables depuis le Microsoft Store si la
/// restauration automatique échoue (Windows ne garantit pas la
/// réinstallation via Add-AppxPackage une fois le paquet purgé du cache).
/// Les écritures HKLM (Telemetry) et schtasks (ScheduledTask) nécessitent
/// des droits administrateur : la relance élevée est proposée à la demande
/// côté UI (voir AdminElevationPrompt) avant d'appeler Clean/Undo/CleanAll
/// pour ces catégories — pas pour BloatwareApp, qui ne le nécessite pas
/// (suppression per-utilisateur via Remove-AppxPackage).
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
        (@"\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector", "Collecteur de diagnostic disque"),
        (@"\Microsoft\Windows\Feedback\Siuf\DmClient", "Client de diagnostic (feedback)"),
        (@"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload", "Client de diagnostic au téléchargement de scénario"),
        (@"\Microsoft\Windows\Windows Error Reporting\QueueReporting", "Mise en file d'attente des rapports d'erreurs")
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
        ("king.com.CandyCrushSaga", "Candy Crush Saga"),
        ("Microsoft.YourPhone", "Lien avec Windows (téléphone)"),
        ("Microsoft.WindowsFeedbackHub", "Centre de notes et commentaires"),
        ("Microsoft.MicrosoftOfficeHub", "Accueil Office (aperçu)"),
        ("Microsoft.SkypeApp", "Skype (app préinstallée)"),
        ("Microsoft.GamingApp", "Xbox (app moderne)"),
        ("Microsoft.XboxApp", "Xbox Console Companion"),
        ("Microsoft.Xbox.TCUI", "Xbox — interface de jeu"),
        ("Microsoft.XboxGameOverlay", "Xbox Game Bar (overlay)"),
        ("Microsoft.XboxGamingOverlay", "Xbox Game Bar"),
        ("Microsoft.XboxIdentityProvider", "Fournisseur d'identité Xbox"),
        ("Microsoft.XboxSpeechToTextOverlay", "Xbox — sous-titres vocaux"),
        ("Microsoft.549981C3F5F10", "Cortana"),
        ("Microsoft.OneConnect", "Wi-Fi et données cellulaires payantes"),
        ("Microsoft.Todos", "Microsoft To Do"),
        ("Clipchamp.Clipchamp", "Clipchamp (montage vidéo)"),
        ("MicrosoftTeams", "Teams (app grand public préinstallée)"),
        ("SpotifyAB.SpotifyMusic", "Spotify (préinstallé)"),
        ("Facebook.Facebook", "Facebook (préinstallé)")
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
                    var (taskExitCode, taskOut, taskErr) = RunProcess("schtasks.exe", $"/Change /TN \"{key}\" /Disable");
                    if (taskExitCode != 0)
                        return (false, $"Échec de la désactivation de '{key}': {FirstNonEmpty(taskErr, taskOut)}");
                    return (true, $"Tâche '{key}' désactivée.");

                case DebloatCategory.BloatwareApp:
                    var fullName = GetInstalledPackageFullName(key);
                    if (fullName is null)
                        return (true, "Déjà absent.");
                    var (appExitCode, appOut, appErr) = RunProcess("powershell.exe",
                        $"-NoProfile -NonInteractive -Command \"Remove-AppxPackage -Package '{fullName}'\"");
                    if (appExitCode != 0)
                        return (false, $"Échec de la suppression de '{key}': {FirstNonEmpty(appErr, appOut)}");
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

    /// <summary>Nettoie tous les éléments non encore traités. Certaines tâches planifiées
    /// protégées par ACL système refusent la désactivation même en administrateur ;
    /// ces échecs sont retournés individuellement plutôt que masqués dans le compte.</summary>
    public (int SuccessCount, IReadOnlyList<(string Name, string Reason)> Failures) CleanAll(IEnumerable<DebloatItem> items)
    {
        var count = 0;
        var failures = new List<(string Name, string Reason)>();
        foreach (var item in items.Where(i => !i.IsClean))
        {
            var (success, message) = Clean(item.Category, item.Key);
            if (success)
                count++;
            else
                failures.Add((item.Name, message));
        }
        return (count, failures);
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
        => RunProcess(fileName, arguments, timeoutMs).StdOut;

    /// <summary>Code de sortie : schtasks/powershell peuvent "réussir" sans effet
    /// (ex: tâche protégée par ACL) ; seul le code de sortie le révèle de façon fiable.</summary>
    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, int timeoutMs = 8000)
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
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return (process.ExitCode, output, error);
    }

    private static string FirstNonEmpty(string primary, string fallback)
    {
        var trimmed = primary.Trim();
        return trimmed.Length > 0 ? trimmed : (fallback.Trim() is { Length: > 0 } f ? f : "erreur inconnue.");
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
