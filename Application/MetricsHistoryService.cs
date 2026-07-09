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
                CREATE TABLE IF NOT EXISTS thermal_daily(
                    day INTEGER NOT NULL,
                    bucket INTEGER NOT NULL,
                    avg_temp REAL NOT NULL,
                    samples INTEGER NOT NULL,
                    PRIMARY KEY(day, bucket));
                CREATE TABLE IF NOT EXISTS disk_wear_daily(
                    day INTEGER NOT NULL,
                    disk_name TEXT NOT NULL,
                    wear_percent INTEGER NOT NULL,
                    PRIMARY KEY(day, disk_name));
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
            RollupThermalDailyIfDue();
            RecordDiskWearIfDue();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Écriture de l'échantillon de métriques impossible", ex);
        }
    }

    private DateTime _lastDiskWearSnapshot = DateTime.MinValue;

    /// <summary>
    /// Enregistre une fois par jour l'usure de chaque SSD/SCM détecté, pour
    /// constituer un historique exploitable dans un dossier de garantie
    /// (contrairement à SsdWearService.Analyze(), qui n'expose qu'un
    /// instantané courant sans mémoire du passé).
    /// </summary>
    internal void RecordDiskWearIfDue()
    {
        if (_connection is null || DateTime.UtcNow - _lastDiskWearSnapshot < TimeSpan.FromHours(24))
            return;

        _lastDiskWearSnapshot = DateTime.UtcNow;

        try
        {
            var today = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
            foreach (var disk in SsdWearService.Analyze())
            {
                if (disk.WearPercent is not { } wear)
                    continue;

                using var command = _connection.CreateCommand();
                command.CommandText = """
                    INSERT OR REPLACE INTO disk_wear_daily(day, disk_name, wear_percent)
                    VALUES ($day, $name, $wear)
                    """;
                command.Parameters.AddWithValue("$day", today);
                command.Parameters.AddWithValue("$name", disk.Name);
                command.Parameters.AddWithValue("$wear", wear);
                command.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Enregistrement quotidien de l'usure disque impossible", ex);
        }
    }

    private DateTime _lastThermalRollup = DateTime.MinValue;

    /// <summary>
    /// Agrège une fois par heure les températures CPU de la veille par palier
    /// de charge (0-30 %, 30-70 %, 70-100 %) dans thermal_daily. Contrairement
    /// aux échantillons bruts (purgés à 8 jours), ces agrégats sont conservés
    /// longtemps : c'est ce qui permet de détecter une dérive sur des semaines.
    /// </summary>
    internal void RollupThermalDailyIfDue()
    {
        if (_connection is null || DateTime.UtcNow - _lastThermalRollup < TimeSpan.FromHours(1))
            return;

        _lastThermalRollup = DateTime.UtcNow;

        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO thermal_daily(day, bucket, avg_temp, samples)
                SELECT timestamp / 86400,
                       CASE WHEN cpu < 30 THEN 0 WHEN cpu < 70 THEN 1 ELSE 2 END AS bucket,
                       AVG(cpu_temp),
                       COUNT(*)
                FROM metric_samples
                WHERE cpu_temp IS NOT NULL
                  AND timestamp / 86400 < $today
                  AND timestamp / 86400 NOT IN (SELECT DISTINCT day FROM thermal_daily)
                GROUP BY timestamp / 86400, bucket
                """;
            command.Parameters.AddWithValue("$today", DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Agrégation thermique quotidienne impossible", ex);
        }
    }

    private static readonly string[] LoadBucketLabels = ["charge faible (0-30 %)", "charge moyenne (30-70 %)", "charge forte (70-100 %)"];

    /// <summary>
    /// Compare la température moyenne récente (7 derniers jours) à la référence
    /// (agrégats d'au moins 21 jours d'ancienneté) à charge comparable.
    /// </summary>
    public IReadOnlyList<string> GetThermalDriftWarnings()
    {
        if (_connection is null)
            return [];

        try
        {
            var rows = new List<(long Day, int Bucket, double AvgTemp, int Samples)>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT day, bucket, avg_temp, samples FROM thermal_daily";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    rows.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetDouble(2), reader.GetInt32(3)));
            }

            return ComputeThermalDrift(rows, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Analyse de dérive thermique impossible", ex);
            return [];
        }
    }

    /// <summary>Calcul pur, testable sans base : dérive = récent (≤7 j) vs référence (≥21 j), ≥3 jours de données de chaque côté.</summary>
    internal static IReadOnlyList<string> ComputeThermalDrift(
        IReadOnlyList<(long Day, int Bucket, double AvgTemp, int Samples)> rows, long todayDayNumber)
    {
        const double DriftThresholdC = 5;
        const int MinDaysEachSide = 3;

        var warnings = new List<string>();

        foreach (var bucket in rows.GroupBy(r => r.Bucket).OrderBy(g => g.Key))
        {
            var recent = bucket.Where(r => todayDayNumber - r.Day <= 7).ToList();
            var baseline = bucket.Where(r => todayDayNumber - r.Day >= 21).ToList();
            if (recent.Count < MinDaysEachSide || baseline.Count < MinDaysEachSide)
                continue;

            var recentAvg = recent.Average(r => r.AvgTemp);
            var baselineAvg = baseline.Average(r => r.AvgTemp);
            var delta = recentAvg - baselineAvg;

            if (delta >= DriftThresholdC && bucket.Key is >= 0 and <= 2)
            {
                warnings.Add(
                    $"CPU en {LoadBucketLabels[bucket.Key]} : {recentAvg:F1}°C en moyenne cette semaine contre {baselineAvg:F1}°C il y a plus de 3 semaines (+{delta:F1}°C). "
                    + "Pâte thermique fatiguée ou radiateur encrassé probables.");
            }
        }

        return warnings;
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

    /// <summary>
    /// Exporte l'historique long terme (température CPU par palier de charge
    /// + usure des disques) en CSV — utile pour un dossier de garantie,
    /// contrairement à ExportToCsv qui se limite aux 8 jours de rétention
    /// des échantillons bruts.
    /// </summary>
    public int ExportWarrantyReportToCsv(string filePath)
    {
        if (_connection is null)
        {
            File.WriteAllText(filePath, BuildWarrantyCsv([], []), Encoding.UTF8);
            return 0;
        }

        var thermalRows = new List<(long Day, int Bucket, double AvgTemp, int Samples)>();
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT day, bucket, avg_temp, samples FROM thermal_daily";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                thermalRows.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetDouble(2), reader.GetInt32(3)));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique thermique pour export impossible", ex);
        }

        var diskWearRows = new List<(long Day, string DiskName, int WearPercent)>();
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT day, disk_name, wear_percent FROM disk_wear_daily";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                diskWearRows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2)));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique d'usure disque pour export impossible", ex);
        }

        var csv = BuildWarrantyCsv(thermalRows, diskWearRows);
        File.WriteAllText(filePath, csv, Encoding.UTF8);
        return thermalRows.Count + diskWearRows.Count;
    }

    internal static string BuildWarrantyCsv(
        IReadOnlyList<(long Day, int Bucket, double AvgTemp, int Samples)> thermalRows,
        IReadOnlyList<(long Day, string DiskName, int WearPercent)> diskWearRows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Categorie,Detail,Valeur");

        foreach (var row in thermalRows.OrderBy(r => r.Day).ThenBy(r => r.Bucket))
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(row.Day * 86400).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sb.AppendLine($"{date},Temperature CPU (C),{LoadBucketLabels[row.Bucket]},{row.AvgTemp.ToString("F1", CultureInfo.InvariantCulture)}");
        }

        foreach (var row in diskWearRows.OrderBy(r => r.Day).ThenBy(r => r.DiskName))
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(row.Day * 86400).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sb.AppendLine($"{date},Usure disque (%),{EscapeCsvField(row.DiskName)},{row.WearPercent.ToString(CultureInfo.InvariantCulture)}");
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string value) =>
        value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;

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
