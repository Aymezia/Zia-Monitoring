using Microsoft.Data.Sqlite;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed record NetworkConsumer(string ProcessName, double TotalMb)
{
    public string Label => TotalMb >= 1024
        ? $"{ProcessName} — {TotalMb / 1024:F1} Go"
        : $"{ProcessName} — {TotalMb:F0} Mo";
}

/// <summary>
/// Historise la consommation réseau par application au fil du temps. Windows
/// ne fournit pas d'API publique fiable pour lire son propre historique
/// (SRUM), et une trace ETW temps réel serait trop lourde : on accumule donc
/// notre propre estimation à partir du débit par processus déjà mesuré à
/// chaque cycle. La couverture se limite au temps où Zia tourne — l'UI le dit
/// clairement. Les écritures SQLite sont regroupées (flush minuté) pour rester
/// légères. Appelé depuis la boucle de monitoring (thread unique).
/// </summary>
public sealed class NetworkUsageHistoryService : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(90);

    private readonly SqliteConnection? _connection;
    private readonly Dictionary<string, double> _pendingBytes = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastFlush = DateTime.MinValue;

    public NetworkUsageHistoryService(string? databaseDirectory = null)
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
                CREATE TABLE IF NOT EXISTS network_usage_daily(
                    day INTEGER NOT NULL,
                    process_name TEXT NOT NULL,
                    bytes REAL NOT NULL,
                    PRIMARY KEY(day, process_name));
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Base d'historique de consommation réseau indisponible", ex);
            _connection = null;
        }
    }

    /// <summary>Accumule l'estimation d'octets de chaque processus réseau pour l'intervalle écoulé.</summary>
    public void Accumulate(IReadOnlyList<ProcessNetworkUsage> processes, double elapsedSeconds)
    {
        if (_connection is null || elapsedSeconds <= 0)
            return;

        foreach (var process in processes)
        {
            if (string.IsNullOrWhiteSpace(process.ProcessName) || process.EstimatedKbps <= 0)
                continue;

            var bytes = EstimateBytes(process.EstimatedKbps, elapsedSeconds);
            _pendingBytes.TryGetValue(process.ProcessName, out var existing);
            _pendingBytes[process.ProcessName] = existing + bytes;
        }

        FlushIfDue();
    }

    /// <summary>KB/s × secondes → octets (KB = 1024). Estimation, débit non instrumenté exactement.</summary>
    internal static double EstimateBytes(double kilobytesPerSecond, double seconds) =>
        kilobytesPerSecond * 1024.0 * seconds;

    private void FlushIfDue()
    {
        if (DateTime.UtcNow - _lastFlush < FlushInterval)
            return;

        Flush();
    }

    public void Flush()
    {
        _lastFlush = DateTime.UtcNow;
        if (_connection is null || _pendingBytes.Count == 0)
            return;

        var day = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
        try
        {
            using var transaction = _connection.BeginTransaction();
            foreach (var (name, bytes) in _pendingBytes)
            {
                using var command = _connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO network_usage_daily(day, process_name, bytes)
                    VALUES ($day, $name, $bytes)
                    ON CONFLICT(day, process_name) DO UPDATE SET bytes = bytes + $bytes
                    """;
                command.Parameters.AddWithValue("$day", day);
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$bytes", bytes);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            _pendingBytes.Clear();

            PruneOld();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Écriture de l'historique de consommation réseau impossible", ex);
        }
    }

    public IReadOnlyList<NetworkConsumer> GetTopConsumers(int days = 30, int top = 15)
    {
        if (_connection is null)
            return [];

        Flush(); // intègre les octets encore en mémoire.

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT process_name, SUM(bytes) AS total
                FROM network_usage_daily
                WHERE day >= $since
                GROUP BY process_name
                ORDER BY total DESC
                LIMIT $top
                """;
            command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400 - days);
            command.Parameters.AddWithValue("$top", top);

            var result = new List<NetworkConsumer>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(new NetworkConsumer(reader.GetString(0), reader.GetDouble(1) / 1024.0 / 1024.0));
            return result;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique de consommation réseau impossible", ex);
            return [];
        }
    }

    private void PruneOld()
    {
        if (_connection is null)
            return;

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM network_usage_daily WHERE day < $limit";
        command.Parameters.AddWithValue("$limit", DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400 - (long)Retention.TotalDays);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Flush();
        _connection?.Dispose();
    }
}
