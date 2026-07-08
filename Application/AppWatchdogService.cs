using System.Diagnostics;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Watchdog d'applications : relance automatiquement une app surveillée si
/// elle disparaît APRÈS avoir été vue en cours d'exécution — approximation
/// d'un crash (on ne relance jamais une app que l'utilisateur n'a pas
/// démarrée lui-même). Garde-fous : 60 s de délai entre relances et 3
/// relances max par heure pour ne pas boucler sur une app qui re-crash.
/// </summary>
public sealed class AppWatchdogService
{
    private static readonly TimeSpan RestartCooldown = TimeSpan.FromSeconds(60);
    private const int MaxRestartsPerHour = 3;

    private sealed class WatchState
    {
        public bool SeenRunning;
        public DateTime? LastRestartAt;
        public List<DateTime> RestartTimes { get; } = [];
    }

    private readonly string _configFile;
    private readonly object _gate = new();
    private List<string> _watchedPaths = [];
    private readonly Dictionary<string, WatchState> _states = new(StringComparer.OrdinalIgnoreCase);

    public AppWatchdogService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _configFile = Path.Combine(dir, "watchdog-apps.json");
        Load();
    }

    public IReadOnlyList<string> GetWatchedPaths()
    {
        lock (_gate) return _watchedPaths.ToList();
    }

    public void Add(string exePath)
    {
        lock (_gate)
        {
            if (_watchedPaths.Any(p => p.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                return;
            _watchedPaths.Add(exePath);
            Save();
        }
    }

    public void Remove(string exePath)
    {
        lock (_gate)
        {
            if (_watchedPaths.RemoveAll(p => p.Equals(exePath, StringComparison.OrdinalIgnoreCase)) > 0)
                Save();
            _states.Remove(exePath);
        }
    }

    /// <summary>Appelé à chaque cycle de monitoring. Retourne les apps relancées à ce cycle.</summary>
    public IReadOnlyList<string> Tick()
    {
        var restarted = new List<string>();
        List<string> watched;
        lock (_gate) watched = _watchedPaths.ToList();
        if (watched.Count == 0)
            return restarted;

        var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
                runningNames.Add(process.ProcessName);
        }

        var now = DateTime.UtcNow;
        foreach (var path in watched)
        {
            var processName = Path.GetFileNameWithoutExtension(path);
            var isRunning = runningNames.Contains(processName);

            lock (_gate)
            {
                if (!_states.TryGetValue(path, out var state))
                    _states[path] = state = new WatchState();

                if (isRunning)
                {
                    state.SeenRunning = true;
                    continue;
                }

                state.RestartTimes.RemoveAll(t => now - t > TimeSpan.FromHours(1));
                if (!ShouldRestart(state.SeenRunning, state.LastRestartAt, state.RestartTimes.Count, now))
                    continue;

                try
                {
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    state.LastRestartAt = now;
                    state.RestartTimes.Add(now);
                    state.SeenRunning = false; // re-armé quand l'app sera revue en vie.
                    restarted.Add(processName);
                }
                catch (Exception ex)
                {
                    Infrastructure.AppLog.Warn($"Watchdog : relance de '{path}' impossible", ex);
                }
            }
        }

        return restarted;
    }

    /// <summary>Décision pure et testable : relancer uniquement après un arrêt inattendu, avec garde-fous.</summary>
    internal static bool ShouldRestart(bool seenRunning, DateTime? lastRestartAt, int restartsLastHour, DateTime now)
    {
        if (!seenRunning)
            return false;

        if (restartsLastHour >= MaxRestartsPerHour)
            return false;

        return lastRestartAt is null || now - lastRestartAt >= RestartCooldown;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_configFile))
                _watchedPaths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_configFile)) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de la liste watchdog impossible", ex);
            _watchedPaths = [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_configFile, JsonSerializer.Serialize(_watchedPaths, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde de la liste watchdog impossible", ex);
        }
    }
}
