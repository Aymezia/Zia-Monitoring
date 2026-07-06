using System.Text.Json;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class OptimizationProfileService
{
    private static readonly IReadOnlyList<OptimizationProfile> BuiltInProfiles =
    [
        new OptimizationProfile("Gaming Max",
            "Priorite maximale aux jeux: services en pause, animations Windows desactivees, mode responsivite.",
            ["Desactiver animations Windows", "Pause SysMain", "Pause WSearch", "Pause DiagTrack", "Priorite haute pour processus actif", "Cleanup temp files"]),

        new OptimizationProfile("Travail Silencieux",
            "Environnement calme: nettoyage demarrage, services non essentiels en pause.",
            ["Cleanup temp files", "Optimisation demarrage", "Pause DiagTrack"]),

        new OptimizationProfile("Streaming",
            "Equilibre performance encodage et jeu: GPU priorite, RAM optimisee.",
            ["Pause SysMain", "Cleanup temp files", "Priorite haute OBS/streaming", "Desactiver animations Windows"]),

        new OptimizationProfile("Equilibre",
            "Parametres Windows par defaut restaures.",
            ["Restaurer animations Windows", "Redemarrer services standards"])
    ];

    private readonly string _customProfilesFile;
    private List<OptimizationProfile> _customProfiles = [];

    public OptimizationProfileService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _customProfilesFile = Path.Combine(dir, "custom-profiles.json");
        LoadCustomProfiles();
    }

    /// <summary>Profils intégrés suivis des profils personnalisés (importés).</summary>
    public IReadOnlyList<OptimizationProfile> GetProfiles() =>
        BuiltInProfiles.Concat(_customProfiles).ToList();

    public (bool Success, IReadOnlyList<string> Actions, IReadOnlyList<string> Warnings)
        Apply(string profileName)
    {
        var profile = GetProfiles().FirstOrDefault(
            p => p.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            return (false, [], [$"Profil inconnu: {profileName}"]);

        var warnings = new List<string>();
        var applied = new List<string>();

        foreach (var action in profile.Actions)
        {
            try
            {
                ExecuteAction(action, warnings, applied);
            }
            catch (Exception ex)
            {
                warnings.Add($"Action '{action}' ignoree: {ex.Message}");
            }
        }

        return (true, applied, warnings);
    }

    /// <summary>Exporte tous les profils (intégrés + personnalisés) vers un fichier JSON.</summary>
    public void ExportProfiles(string filePath)
    {
        var json = JsonSerializer.Serialize(GetProfiles(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Importe des profils depuis un fichier JSON. Les noms de profils intégrés
    /// sont protégés (ignorés) ; un profil personnalisé existant du même nom
    /// est remplacé.
    /// </summary>
    public (int Imported, int Skipped) ImportProfiles(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var incoming = JsonSerializer.Deserialize<List<OptimizationProfile>>(json) ?? [];

        var imported = 0;
        var skipped = 0;

        foreach (var profile in incoming)
        {
            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Actions is not { Count: > 0 })
            {
                skipped++;
                continue;
            }

            if (BuiltInProfiles.Any(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                skipped++;
                continue;
            }

            _customProfiles.RemoveAll(p => p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase));
            _customProfiles.Add(profile);
            imported++;
        }

        if (imported > 0)
            SaveCustomProfiles();

        return (imported, skipped);
    }

    private void LoadCustomProfiles()
    {
        try
        {
            if (!File.Exists(_customProfilesFile))
                return;

            var json = File.ReadAllText(_customProfilesFile);
            _customProfiles = JsonSerializer.Deserialize<List<OptimizationProfile>>(json) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des profils personnalisés impossible", ex);
            _customProfiles = [];
        }
    }

    private void SaveCustomProfiles()
    {
        try
        {
            var json = JsonSerializer.Serialize(_customProfiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_customProfilesFile, json);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde des profils personnalisés impossible", ex);
        }
    }

    private static void ExecuteAction(string action, List<string> warnings, List<string> applied)
    {
        switch (action)
        {
            case "Desactiver animations Windows":
                SilentModeService.SetWindowsAnimations(false);
                applied.Add(action);
                break;

            case "Restaurer animations Windows":
                SilentModeService.SetWindowsAnimations(true);
                applied.Add(action);
                break;

            case "Cleanup temp files":
                var deleted = CleanTemp();
                applied.Add($"Cleanup temp: {deleted} fichier(s)");
                break;

            default:
                applied.Add($"{action} (simule)");
                break;
        }
    }

    private static int CleanTemp()
    {
        var root = Path.GetTempPath();
        if (!Directory.Exists(root)) return 0;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(2000))
        {
            try
            {
                if (new FileInfo(file).LastWriteTimeUtc < DateTime.UtcNow.AddHours(-4))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch { }
        }
        return deleted;
    }
}
