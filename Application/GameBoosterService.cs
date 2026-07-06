using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Game Booster façon Razer Cortex : à la détection d'un jeu, passe le plan
/// d'alimentation en Performances élevées, monte la priorité du jeu et
/// baisse celle des applications de fond connues ; restaure tout à la
/// fermeture. L'état de restauration est persisté sur disque pour survivre
/// à un crash de l'application.
/// </summary>
public sealed class GameBoosterService
{
    private static readonly Guid HighPerformancePlan = Guid.Parse("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    private static readonly string[] BackgroundCandidates =
    [
        "OneDrive", "Teams", "Slack", "Discord", "DiscordPTB", "Spotify",
        "EpicGamesLauncher", "steamwebhelper", "msedge", "chrome", "opera",
        "firefox", "Dropbox", "GoogleDriveFS"
    ];

    private sealed record BoosterState(
        string GameName,
        int GamePid,
        Guid? PreviousPowerPlan,
        Dictionary<int, string> DeprioritizedPids);

    private readonly string _stateFile;
    private BoosterState? _state;

    public GameBoosterService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "game-booster-state.json");

        RecoverFromPreviousRun();
    }

    public bool IsActive => _state is not null;

    /// <summary>Active le boost pour le jeu donné. Retourne un résumé des actions.</summary>
    public IReadOnlyList<string> Activate(int gamePid, string gameName)
    {
        if (_state is not null)
            return [];

        var actions = new List<string>();
        var previousPlan = GetActivePowerPlan();

        if (previousPlan is not null && previousPlan != HighPerformancePlan)
        {
            if (SetActivePowerPlan(HighPerformancePlan))
                actions.Add("Plan d'alimentation → Performances élevées");
            else
                previousPlan = null; // rien à restaurer
        }
        else
        {
            previousPlan = null;
        }

        try
        {
            using var game = Process.GetProcessById(gamePid);
            game.PriorityClass = ProcessPriorityClass.High;
            actions.Add($"Priorité haute pour {gameName}");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Priorité haute impossible pour {gameName}", ex);
        }

        var deprioritized = new Dictionary<int, string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!BackgroundCandidates.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                    continue;

                try
                {
                    if (process.PriorityClass is ProcessPriorityClass.Normal or ProcessPriorityClass.AboveNormal)
                    {
                        process.PriorityClass = ProcessPriorityClass.BelowNormal;
                        deprioritized[process.Id] = process.ProcessName;
                    }
                }
                catch
                {
                    // Processus protégé ou déjà terminé : ignoré.
                }
            }
        }

        if (deprioritized.Count > 0)
            actions.Add($"{deprioritized.Count} application(s) de fond dépriorisée(s)");

        _state = new BoosterState(gameName, gamePid, previousPlan, deprioritized);
        PersistState();

        Infrastructure.AppLog.Info($"Game Booster activé pour {gameName} ({actions.Count} action(s))");
        return actions;
    }

    /// <summary>Restaure plan d'alimentation et priorités.</summary>
    public void Deactivate()
    {
        if (_state is null)
            return;

        var state = _state;
        _state = null;

        if (state.PreviousPowerPlan is { } plan)
            SetActivePowerPlan(plan);

        foreach (var (pid, name) in state.DeprioritizedPids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                // Vérifie que le PID n'a pas été réutilisé par un autre exécutable.
                if (process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)
                    && process.PriorityClass == ProcessPriorityClass.BelowNormal)
                {
                    process.PriorityClass = ProcessPriorityClass.Normal;
                }
            }
            catch
            {
                // Processus terminé entre-temps : rien à restaurer.
            }
        }

        try { File.Delete(_stateFile); } catch { }

        Infrastructure.AppLog.Info($"Game Booster désactivé (fin de {state.GameName})");
    }

    /// <summary>
    /// Si l'app a crashé pendant un boost, restaure au moins le plan
    /// d'alimentation au démarrage suivant.
    /// </summary>
    private void RecoverFromPreviousRun()
    {
        try
        {
            if (!File.Exists(_stateFile))
                return;

            var state = JsonSerializer.Deserialize<BoosterState>(File.ReadAllText(_stateFile));
            if (state?.PreviousPowerPlan is { } plan)
            {
                SetActivePowerPlan(plan);
                Infrastructure.AppLog.Info("Game Booster: plan d'alimentation restauré après un arrêt inattendu");
            }

            File.Delete(_stateFile);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Game Booster: restauration post-crash impossible", ex);
        }
    }

    private void PersistState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_state));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Game Booster: persistance de l'état impossible", ex);
        }
    }

    internal static Guid? ParsePowerPlanGuid(string powercfgOutput)
    {
        var match = Regex.Match(powercfgOutput, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success && Guid.TryParse(match.Value, out var guid) ? guid : null;
    }

    private static Guid? GetActivePowerPlan()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return ParsePowerPlanGuid(output);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du plan d'alimentation actif impossible", ex);
            return null;
        }
    }

    private static bool SetActivePowerPlan(Guid plan)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", $"/setactive {plan}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(3000);
            return process?.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Changement de plan d'alimentation impossible", ex);
            return false;
        }
    }
}
