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

    public IReadOnlyList<OptimizationProfile> GetProfiles() => BuiltInProfiles;

    public (bool Success, IReadOnlyList<string> Actions, IReadOnlyList<string> Warnings)
        Apply(string profileName, BoostEngine boostEngine)
    {
        var profile = BuiltInProfiles.FirstOrDefault(
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
