using System.Diagnostics;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record GameLaunchProfile(string GameProcessName, IReadOnlyList<string> CompanionAppPaths, IReadOnlyList<string> KillOnLaunch)
{
    public string Label => $"{GameProcessName} — {CompanionAppPaths.Count} appli(s) compagnon, {KillOnLaunch.Count} à tuer au lancement";
}

/// <summary>
/// Profils de lancement par jeu : au démarrage d'un jeu configuré, lance des
/// applications compagnon (Discord, OBS, Afterburner...) et termine des
/// process indésirables une seule fois (pas de surveillance continue,
/// contrairement au Game Booster qui ajuste les priorités en continu).
/// </summary>
public sealed class GameLaunchProfileService
{
    private readonly string _profilesFile;
    private readonly object _gate = new();
    private List<GameLaunchProfile> _profiles = [];
    private string? _activeGame;

    public bool IsActive => _activeGame is not null;

    public GameLaunchProfileService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _profilesFile = Path.Combine(dir, "game-launch-profiles.json");
        Load();
    }

    public IReadOnlyList<GameLaunchProfile> GetProfiles()
    {
        lock (_gate) return _profiles.ToList();
    }

    public void SaveProfile(GameLaunchProfile profile)
    {
        lock (_gate)
        {
            _profiles.RemoveAll(p => p.GameProcessName.Equals(profile.GameProcessName, StringComparison.OrdinalIgnoreCase));
            _profiles.Add(profile);
            Save();
        }
    }

    public void DeleteProfile(string gameProcessName)
    {
        lock (_gate)
        {
            if (_profiles.RemoveAll(p => p.GameProcessName.Equals(gameProcessName, StringComparison.OrdinalIgnoreCase)) > 0)
                Save();
        }
    }

    /// <summary>Lance les compagnons + tue les process configurés pour ce jeu. Retourne un résumé des actions.</summary>
    public IReadOnlyList<string> Activate(string gameProcessName)
    {
        _activeGame = gameProcessName;
        var actions = new List<string>();

        GameLaunchProfile? profile;
        lock (_gate)
        {
            profile = _profiles.FirstOrDefault(p => p.GameProcessName.Equals(gameProcessName, StringComparison.OrdinalIgnoreCase));
        }
        if (profile is null)
            return actions;

        foreach (var path in profile.CompanionAppPaths)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                actions.Add($"Lancé : {Path.GetFileNameWithoutExtension(path)}");
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn($"Profil de lancement '{gameProcessName}': impossible de lancer '{path}'", ex);
            }
        }

        foreach (var name in profile.KillOnLaunch)
        {
            var normalized = ProcessRuleService.NormalizeName(name);
            foreach (var process in Process.GetProcessesByName(normalized))
            {
                using (process)
                {
                    try
                    {
                        process.Kill();
                        actions.Add($"Arrêté : {normalized}");
                    }
                    catch (Exception ex)
                    {
                        Infrastructure.AppLog.Warn($"Profil de lancement '{gameProcessName}': impossible d'arrêter '{normalized}'", ex);
                    }
                }
            }
        }

        return actions;
    }

    public void Deactivate() => _activeGame = null;

    private void Load()
    {
        try
        {
            if (File.Exists(_profilesFile))
                _profiles = JsonSerializer.Deserialize<List<GameLaunchProfile>>(File.ReadAllText(_profilesFile)) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des profils de lancement impossible", ex);
            _profiles = [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_profilesFile, JsonSerializer.Serialize(_profiles, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde des profils de lancement impossible", ex);
        }
    }
}
