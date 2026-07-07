using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed record MetricSampleRow(
    DateTime Timestamp,
    double CpuPercent,
    double MemoryUsedMb,
    double MemoryTotalMb,
    double? CpuTemperatureC,
    double? GpuTemperatureC,
    double? GpuUsagePercent);

/// <summary>
/// Persiste un échantillon de métriques toutes les 15 s dans une base SQLite
/// locale, pour que les graphes 24 h / 7 j survivent aux redémarrages.
/// Toutes les méthodes sont appelées depuis la boucle de monitoring (thread
/// unique) ; la connexion n'est donc pas partagée entre threads.
/// </summary>
public sealed class MetricsHistoryService : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RetentionWindow = TimeSpan.FromDays(8);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private readonly SqliteConnection? _connection;
    private DateTime _lastSampleAt = DateTime.MinValue;
    private DateTime _lastPruneAt = DateTime.MinValue;

    public MetricsHistoryService(string? databaseDirectory = null)
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
                CREATE TABLE IF NOT EXISTS metric_samples(
                    timestamp INTEGER NOT NULL,
                    cpu REAL NOT NULL,
                    mem_used REAL NOT NULL,
                    mem_total REAL NOT NULL,
                    cpu_temp REAL NULL,
                    gpu_temp REAL NULL,
                    gpu_usage REAL NULL);
                CREATE INDEX IF NOT EXISTS ix_metric_samples_timestamp ON metric_samples(timestamp);
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Base d'historique des métriques indisponible", ex);
            _connection = null;
        }
    }

    /// <summary>Enregistre un échantillon si l'intervalle est écoulé (sinon no-op).</summary>
    public void Record(SystemSnapshot snapshot)
    {
        if (_connection is null || DateTime.UtcNow - _lastSampleAt < SampleInterval)
            return;

        _lastSampleAt = DateTime.UtcNow;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO metric_samples(timestamp, cpu, mem_used, mem_total, cpu_temp, gpu_temp, gpu_usage)
                VALUES ($ts, $cpu, $memUsed, $memTotal, $cpuTemp, $gpuTemp, $gpuUsage)
                """;
            command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            command.Parameters.AddWithValue("$cpu", snapshot.CpuPercent);
            command.Parameters.AddWithValue("$memUsed", snapshot.MemoryUsedMb);
            command.Parameters.AddWithValue("$memTotal", snapshot.MemoryTotalMb);
            command.Parameters.AddWithValue("$cpuTemp", (object?)snapshot.CpuTemperatureC ?? DBNull.Value);
            command.Parameters.AddWithValue("$gpuTemp", (object?)snapshot.GpuTemperatureC ?? DBNull.Value);
            command.Parameters.AddWithValue("$gpuUsage", (object?)snapshot.GpuUsagePercent ?? DBNull.Value);
            command.ExecuteNonQuery();

            PruneIfDue();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Écriture de l'échantillon de métriques impossible", ex);
        }
    }

    /// <summary>Moyennes CPU par heure sur les dernières 24 h (heures sans données omises).</summary>
    public IReadOnlyList<double> GetCpuHourlyAverages24h() =>
        QueryAverages(bucketSeconds: 3600, windowSeconds: 24 * 3600);

    /// <summary>Moyennes CPU par jour sur les 7 derniers jours (jours sans données omis).</summary>
    public IReadOnlyList<double> GetCpuDailyAverages7d() =>
        QueryAverages(bucketSeconds: 24 * 3600, windowSeconds: 7 * 24 * 3600);

    private IReadOnlyList<double> QueryAverages(long bucketSeconds, long windowSeconds)
    {
        if (_connection is null)
            return Array.Empty<double>();

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT AVG(cpu)
                FROM metric_samples
                WHERE timestamp >= $since
                GROUP BY timestamp / $bucket
                ORDER BY timestamp / $bucket
                """;
            command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - windowSeconds);
            command.Parameters.AddWithValue("$bucket", bucketSeconds);

            var result = new List<double>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.Add(Math.Round(reader.GetDouble(0), 1));
            return result;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique des métriques impossible", ex);
            return Array.Empty<double>();
        }
    }

    /// <summary>Échantillons bruts des derniers <paramref name="days"/> jours (8 max, limite de rétention).</summary>
    public IReadOnlyList<MetricSampleRow> GetRawSamples(int days = 8)
    {
        if (_connection is null)
            return Array.Empty<MetricSampleRow>();

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT timestamp, cpu, mem_used, mem_total, cpu_temp, gpu_temp, gpu_usage
                FROM metric_samples
                WHERE timestamp >= $since
                ORDER BY timestamp ASC
                """;
            command.Parameters.AddWithValue("$since", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - days * 86400L);

            var result = new List<MetricSampleRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                result.Add(new MetricSampleRow(
                    DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(0)).LocalDateTime,
                    reader.GetDouble(1),
                    reader.GetDouble(2),
                    reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    reader.IsDBNull(5) ? null : reader.GetDouble(5),
                    reader.IsDBNull(6) ? null : reader.GetDouble(6)));
            }
            return result;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des échantillons pour export impossible", ex);
            return Array.Empty<MetricSampleRow>();
        }
    }

    /// <summary>Exporte l'historique brut en CSV. Retourne le nombre de lignes écrites.</summary>
    public int ExportToCsv(string filePath, int days = 8)
    {
        var rows = GetRawSamples(days);
        File.WriteAllText(filePath, BuildCsv(rows), Encoding.UTF8);
        return rows.Count;
    }

    /// <summary>Exporte l'historique brut en JSON. Retourne le nombre d'entrées écrites.</summary>
    public int ExportToJson(string filePath, int days = 8)
    {
        var rows = GetRawSamples(days);
        File.WriteAllText(filePath, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        return rows.Count;
    }

    internal static string BuildCsv(IReadOnlyList<MetricSampleRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,CpuPercent,MemoryUsedMb,MemoryTotalMb,CpuTemperatureC,GpuTemperatureC,GpuUsagePercent");

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',',
                row.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                row.CpuPercent.ToString(CultureInfo.InvariantCulture),
                row.MemoryUsedMb.ToString(CultureInfo.InvariantCulture),
                row.MemoryTotalMb.ToString(CultureInfo.InvariantCulture),
                row.CpuTemperatureC?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.GpuTemperatureC?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.GpuUsagePercent?.ToString(CultureInfo.InvariantCulture) ?? ""));
        }

        return sb.ToString();
    }

    private void PruneIfDue()
    {
        if (_connection is null || DateTime.UtcNow - _lastPruneAt < PruneInterval)
            return;

        _lastPruneAt = DateTime.UtcNow;

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM metric_samples WHERE timestamp < $limit";
        command.Parameters.AddWithValue("$limit",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)RetentionWindow.TotalSeconds);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
