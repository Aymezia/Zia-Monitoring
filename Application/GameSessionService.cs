using Microsoft.Data.Sqlite;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed record GameSessionReport(
    string GameName,
    DateTime StartedAt,
    DateTime EndedAt,
    double AvgFps,
    double OnePercentLowFps,
    double MaxCpuTempC,
    double MaxGpuTempC,
    double MaxRamMb,
    double MaxVramMb)
{
    public TimeSpan Duration => EndedAt - StartedAt;
    public string DateLabel => StartedAt.ToString("dd/MM HH:mm");
    public string DurationLabel => Duration.TotalHours >= 1
        ? $"{(int)Duration.TotalHours}h{Duration.Minutes:D2}"
        : $"{Duration.Minutes} min";
    public string FpsLabel => AvgFps > 0
        ? $"{AvgFps:F0} FPS · 1% low {OnePercentLowFps:F0}"
        : "FPS N/A";
    public string TempLabel => MaxCpuTempC > 0 || MaxGpuTempC > 0
        ? $"CPU {MaxCpuTempC:F0}°C · GPU {MaxGpuTempC:F0}°C max"
        : "Temp N/A";
    public string RamLabel => $"RAM {MaxRamMb / 1024:F1} GB max";
}

/// <summary>Récapitulatif façon "Wrapped" agrégé sur les sessions d'une année.</summary>
public sealed record WrappedSummary(
    int Year,
    TimeSpan TotalPlaytime,
    int TotalSessions,
    string? MostPlayedGame,
    TimeSpan MostPlayedGameDuration,
    double AverageFps,
    double MaxCpuTempRecorded,
    double MaxGpuTempRecorded,
    string? LongestSessionGame,
    TimeSpan LongestSessionDuration)
{
    public string PlaytimeLabel => $"{(int)TotalPlaytime.TotalHours} h {TotalPlaytime.Minutes:D2}";
    public string MostPlayedLabel => MostPlayedGame is null ? "-" : $"{MostPlayedGame} ({(int)MostPlayedGameDuration.TotalHours}h{MostPlayedGameDuration.Minutes:D2})";
    public string LongestSessionLabel => LongestSessionGame is null ? "-" : $"{LongestSessionGame} — {(int)LongestSessionDuration.TotalHours}h{LongestSessionDuration.Minutes:D2}";
    public string AvgFpsLabel => AverageFps > 0 ? $"{AverageFps:F0} FPS" : "N/A";
    public string MaxTempLabel => $"CPU {MaxCpuTempRecorded:F0}°C · GPU {MaxGpuTempRecorded:F0}°C";
}

/// <summary>
/// Suit la session de jeu active (FPS, températures et mémoire max) et
/// produit un rapport persisté en SQLite à la fermeture du jeu — façon
/// « session report » de NZXT CAM. Alimente aussi les statistiques de
/// temps de jeu hebdomadaires. Appelé uniquement depuis la boucle de
/// monitoring (thread unique).
/// </summary>
public sealed class GameSessionService : IDisposable
{
    private static readonly TimeSpan MinimumSessionDuration = TimeSpan.FromMinutes(3);

    private readonly SqliteConnection? _connection;

    private string? _currentGame;
    private DateTime _startedAt;
    private readonly List<double> _fpsSamples = [];
    private double _maxCpuTemp;
    private double _maxGpuTemp;
    private double _maxRam;
    private double _maxVram;
    private DateTime _lastGoalNotification = DateTime.MinValue;

    public GameSessionService(string? databaseDirectory = null)
    {
        try
        {
            var dir = databaseDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ZiaMonitoring");
            Directory.CreateDirectory(dir);

            _connection = new SqliteConnection($"Data Source={Path.Combine(dir, "metrics.db")}");
            _connection.Open();

            using var command = _connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS game_sessions(
                    game TEXT NOT NULL,
                    started_at INTEGER NOT NULL,
                    ended_at INTEGER NOT NULL,
                    avg_fps REAL NOT NULL,
                    low1_fps REAL NOT NULL,
                    max_cpu_temp REAL NOT NULL,
                    max_gpu_temp REAL NOT NULL,
                    max_ram_mb REAL NOT NULL,
                    max_vram_mb REAL NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_game_sessions_started ON game_sessions(started_at);
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Base des sessions de jeu indisponible", ex);
            _connection = null;
        }
    }

    /// <summary>
    /// À appeler à chaque cycle. Retourne le rapport final quand une session
    /// vient de se terminer, sinon null.
    /// </summary>
    public GameSessionReport? OnMonitoringTick(ActiveGameSession? activeGame, SystemSnapshot snapshot)
    {
        if (activeGame is not null)
        {
            if (!activeGame.GameName.Equals(_currentGame, StringComparison.OrdinalIgnoreCase))
            {
                // Un autre jeu démarre pendant qu'un premier tournait : clôture d'abord.
                var previous = _currentGame is not null ? FinalizeSession() : null;
                StartSession(activeGame.GameName);
                Accumulate(snapshot);
                return previous;
            }

            Accumulate(snapshot);
            return null;
        }

        return _currentGame is not null ? FinalizeSession() : null;
    }

    /// <summary>Sessions les plus récentes, la dernière en premier.</summary>
    public IReadOnlyList<GameSessionReport> GetRecentSessions(int count = 12)
    {
        if (_connection is null)
            return Array.Empty<GameSessionReport>();

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT game, started_at, ended_at, avg_fps, low1_fps,
                       max_cpu_temp, max_gpu_temp, max_ram_mb, max_vram_mb
                FROM game_sessions
                ORDER BY started_at DESC
                LIMIT $count
                """;
            command.Parameters.AddWithValue("$count", count);

            var result = new List<GameSessionReport>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new GameSessionReport(
                    reader.GetString(0),
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)).LocalDateTime,
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(2)).LocalDateTime,
                    reader.GetDouble(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8)));
            }
            return result;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des sessions de jeu impossible", ex);
            return Array.Empty<GameSessionReport>();
        }
    }

    /// <summary>Temps de jeu cumulé depuis lundi, y compris la session en cours.</summary>
    public TimeSpan GetWeeklyPlaytime()
    {
        var total = TimeSpan.Zero;

        if (_connection is not null)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT COALESCE(SUM(ended_at - started_at), 0) FROM game_sessions WHERE started_at >= $since";
                command.Parameters.AddWithValue("$since", new DateTimeOffset(StartOfWeek()).ToUnixTimeSeconds());
                total = TimeSpan.FromSeconds(Convert.ToDouble(command.ExecuteScalar()));
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("Calcul du temps de jeu hebdomadaire impossible", ex);
            }
        }

        if (_currentGame is not null)
            total += DateTime.Now - _startedAt;

        return total;
    }

    /// <summary>Temps de jeu par jeu depuis lundi (session en cours incluse).</summary>
    public IReadOnlyList<(string Game, TimeSpan Total)> GetWeeklyPlaytimeByGame()
    {
        var byGame = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase);

        if (_connection is not null)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = """
                    SELECT game, SUM(ended_at - started_at)
                    FROM game_sessions
                    WHERE started_at >= $since
                    GROUP BY game
                    """;
                command.Parameters.AddWithValue("$since", new DateTimeOffset(StartOfWeek()).ToUnixTimeSeconds());
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    byGame[reader.GetString(0)] = TimeSpan.FromSeconds(reader.GetDouble(1));
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("Calcul du temps de jeu par jeu impossible", ex);
            }
        }

        if (_currentGame is not null)
        {
            byGame.TryGetValue(_currentGame, out var current);
            byGame[_currentGame] = current + (DateTime.Now - _startedAt);
        }

        return byGame
            .OrderByDescending(x => x.Value)
            .Select(x => (x.Key, x.Value))
            .ToList();
    }

    /// <summary>
    /// Vrai si l'objectif hebdo est dépassé et qu'aucune notification n'a
    /// été envoyée aujourd'hui (une seule alerte par jour).
    /// </summary>
    public bool ShouldNotifyWeeklyGoalExceeded(double goalHours)
    {
        if (goalHours <= 0 || _lastGoalNotification.Date == DateTime.Today)
            return false;

        if (GetWeeklyPlaytime() < TimeSpan.FromHours(goalHours))
            return false;

        _lastGoalNotification = DateTime.Now;
        return true;
    }

    private void StartSession(string gameName)
    {
        _currentGame = gameName;
        _startedAt = DateTime.Now;
        _fpsSamples.Clear();
        _maxCpuTemp = 0;
        _maxGpuTemp = 0;
        _maxRam = 0;
        _maxVram = 0;
    }

    private void Accumulate(SystemSnapshot snapshot)
    {
        if (snapshot.EstimatedFps > 0)
            _fpsSamples.Add(snapshot.EstimatedFps);

        _maxCpuTemp = Math.Max(_maxCpuTemp, snapshot.CpuTemperatureC ?? 0);
        _maxGpuTemp = Math.Max(_maxGpuTemp, snapshot.GpuTemperatureC ?? 0);
        _maxRam = Math.Max(_maxRam, snapshot.MemoryUsedMb);
        _maxVram = Math.Max(_maxVram, snapshot.VramUsedMb);
    }

    private GameSessionReport? FinalizeSession()
    {
        var game = _currentGame!;
        var startedAt = _startedAt;
        _currentGame = null;

        var endedAt = DateTime.Now;
        if (endedAt - startedAt < MinimumSessionDuration)
            return null;

        var report = new GameSessionReport(
            game, startedAt, endedAt,
            AvgFps: _fpsSamples.Count > 0 ? _fpsSamples.Average() : 0,
            OnePercentLowFps: Percentile(_fpsSamples, 0.01),
            MaxCpuTempC: _maxCpuTemp,
            MaxGpuTempC: _maxGpuTemp,
            MaxRamMb: _maxRam,
            MaxVramMb: _maxVram);

        Persist(report);
        return report;
    }

    private void Persist(GameSessionReport report)
    {
        if (_connection is null)
            return;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO game_sessions(game, started_at, ended_at, avg_fps, low1_fps,
                                          max_cpu_temp, max_gpu_temp, max_ram_mb, max_vram_mb)
                VALUES ($game, $start, $end, $avg, $low1, $cpuT, $gpuT, $ram, $vram)
                """;
            command.Parameters.AddWithValue("$game", report.GameName);
            command.Parameters.AddWithValue("$start", new DateTimeOffset(report.StartedAt).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$end", new DateTimeOffset(report.EndedAt).ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$avg", Math.Round(report.AvgFps, 1));
            command.Parameters.AddWithValue("$low1", Math.Round(report.OnePercentLowFps, 1));
            command.Parameters.AddWithValue("$cpuT", Math.Round(report.MaxCpuTempC, 1));
            command.Parameters.AddWithValue("$gpuT", Math.Round(report.MaxGpuTempC, 1));
            command.Parameters.AddWithValue("$ram", Math.Round(report.MaxRamMb, 0));
            command.Parameters.AddWithValue("$vram", Math.Round(report.MaxVramMb, 0));
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Enregistrement du rapport de session impossible", ex);
        }
    }

    /// <summary>Percentile bas (0.01 = 1% low) ; 0 si pas assez d'échantillons.</summary>
    internal static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        if (samples.Count == 0)
            return 0;

        var sorted = samples.Order().ToList();
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    internal static DateTime StartOfWeek()
    {
        var today = DateTime.Today;
        var offset = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return today.AddDays(-offset);
    }

    /// <summary>
    /// Récapitulatif façon "Wrapped" sur toutes les sessions de l'année
    /// donnée. Retourne null si aucune session enregistrée (jamais "0h" —
    /// distingue l'absence de données d'une vraie valeur nulle).
    /// </summary>
    public WrappedSummary? GetYearlyWrapped(int year)
    {
        if (_connection is null)
            return null;

        try
        {
            var start = new DateTimeOffset(new DateTime(year, 1, 1)).ToUnixTimeSeconds();
            var end = new DateTimeOffset(new DateTime(year + 1, 1, 1)).ToUnixTimeSeconds();

            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT game, started_at, ended_at, avg_fps, max_cpu_temp, max_gpu_temp
                FROM game_sessions
                WHERE started_at >= $start AND started_at < $end
                """;
            command.Parameters.AddWithValue("$start", start);
            command.Parameters.AddWithValue("$end", end);

            var sessions = new List<WrappedSessionRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                sessions.Add(new WrappedSessionRow(
                    reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                    reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5)));
            }

            return BuildWrappedSummary(year, sessions);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Calcul du récapitulatif annuel impossible", ex);
            return null;
        }
    }

    internal readonly record struct WrappedSessionRow(
        string Game, long StartedAt, long EndedAt, double AvgFps, double MaxCpuTemp, double MaxGpuTemp);

    internal static WrappedSummary? BuildWrappedSummary(int year, IReadOnlyList<WrappedSessionRow> sessions)
    {
        if (sessions.Count == 0)
            return null;

        var totalSeconds = sessions.Sum(s => s.EndedAt - s.StartedAt);

        var byGame = sessions
            .GroupBy(s => s.Game, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Game: g.Key, Duration: TimeSpan.FromSeconds(g.Sum(s => s.EndedAt - s.StartedAt))))
            .OrderByDescending(g => g.Duration)
            .First();

        var longest = sessions.OrderByDescending(s => s.EndedAt - s.StartedAt).First();
        var validFps = sessions.Where(s => s.AvgFps > 0).Select(s => s.AvgFps).ToList();

        return new WrappedSummary(
            year,
            TimeSpan.FromSeconds(totalSeconds),
            sessions.Count,
            byGame.Game,
            byGame.Duration,
            validFps.Count > 0 ? validFps.Average() : 0,
            sessions.Max(s => s.MaxCpuTemp),
            sessions.Max(s => s.MaxGpuTemp),
            longest.Game,
            TimeSpan.FromSeconds(longest.EndedAt - longest.StartedAt));
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
