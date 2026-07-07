using System.Management;
using Microsoft.Data.Sqlite;

namespace ZiaMonitoring_App.Application;

public sealed record SmartAttributeReading(string Disk, int AttributeId, string AttributeName, long RawValue);

public sealed record SmartTrendWarning(string Disk, string AttributeName, long Previous, long Current, int DaysSpan)
{
    public string Label => $"{Disk}: {AttributeName} passé de {Previous} à {Current} en {DaysSpan} jour(s) — envisagez une sauvegarde et un remplacement.";
}

/// <summary>
/// SMART prédictif : au lieu de n'alerter que sur le bit « défaillance
/// imminente » (souvent trop tard), enregistre chaque jour les attributs
/// critiques bruts (secteurs réalloués, en attente, incorrigibles…) et
/// alerte dès qu'une valeur AUGMENTE — le signal précoce standard de
/// dégradation mécanique. Les disques NVMe n'exposent pas toujours ces
/// attributs via WMI : dégradation silencieuse dans ce cas.
/// </summary>
public sealed class SmartTrendService : IDisposable
{
    /// <summary>Attributs dont la moindre augmentation est un signal de dégradation.</summary>
    internal static readonly IReadOnlyDictionary<int, string> CriticalAttributes = new Dictionary<int, string>
    {
        [5] = "Secteurs réalloués",
        [187] = "Erreurs incorrigibles signalées",
        [196] = "Événements de réallocation",
        [197] = "Secteurs en attente de réallocation",
        [198] = "Secteurs incorrigibles (offline)"
    };

    private readonly SqliteConnection? _connection;
    private DateTime _lastDailyCheckDate = DateTime.MinValue;

    public SmartTrendService(string? databaseDirectory = null)
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
                CREATE TABLE IF NOT EXISTS smart_snapshots(
                    disk TEXT NOT NULL,
                    attribute_id INTEGER NOT NULL,
                    raw_value INTEGER NOT NULL,
                    recorded_at INTEGER NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_smart_snapshots ON smart_snapshots(disk, attribute_id, recorded_at);
                """;
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Base des snapshots SMART indisponible", ex);
            _connection = null;
        }
    }

    /// <summary>Vrai une fois par jour : déclenche le snapshot + l'analyse.</summary>
    public bool IsDailyCheckDue()
    {
        if (_lastDailyCheckDate == DateTime.Today)
            return false;

        _lastDailyCheckDate = DateTime.Today;
        return true;
    }

    /// <summary>Lit les attributs SMART critiques de tous les disques exposés par WMI.</summary>
    public IReadOnlyList<SmartAttributeReading> ReadCurrent()
    {
        var readings = new List<SmartAttributeReading>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi", "SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData");

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var disk = ShortenInstanceName(obj["InstanceName"]?.ToString() ?? "Disque");
                if (obj["VendorSpecific"] is not byte[] data)
                    continue;

                foreach (var (id, raw) in ParseVendorSpecific(data))
                {
                    if (CriticalAttributes.TryGetValue(id, out var name))
                        readings.Add(new SmartAttributeReading(disk, id, name, raw));
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des attributs SMART impossible", ex);
        }

        return readings;
    }

    /// <summary>
    /// Enregistre le snapshot du jour puis compare aux valeurs les plus
    /// anciennes des 30 derniers jours. Retourne les dégradations détectées.
    /// </summary>
    public IReadOnlyList<SmartTrendWarning> RecordAndAnalyze()
    {
        var current = ReadCurrent();
        if (_connection is null || current.Count == 0)
            return Array.Empty<SmartTrendWarning>();

        var warnings = new List<SmartTrendWarning>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
        {
            foreach (var reading in current)
            {
                using (var select = _connection.CreateCommand())
                {
                    select.CommandText = """
                        SELECT raw_value, recorded_at FROM smart_snapshots
                        WHERE disk = $disk AND attribute_id = $attr AND recorded_at >= $since
                        ORDER BY recorded_at ASC LIMIT 1
                        """;
                    select.Parameters.AddWithValue("$disk", reading.Disk);
                    select.Parameters.AddWithValue("$attr", reading.AttributeId);
                    select.Parameters.AddWithValue("$since", now - 30L * 24 * 3600);

                    using var reader = select.ExecuteReader();
                    if (reader.Read())
                    {
                        var previous = reader.GetInt64(0);
                        var recordedAt = reader.GetInt64(1);
                        if (reading.RawValue > previous)
                        {
                            var days = Math.Max(1, (int)((now - recordedAt) / 86400));
                            warnings.Add(new SmartTrendWarning(
                                reading.Disk, reading.AttributeName, previous, reading.RawValue, days));
                        }
                    }
                }

                using var insert = _connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO smart_snapshots(disk, attribute_id, raw_value, recorded_at)
                    VALUES ($disk, $attr, $raw, $at)
                    """;
                insert.Parameters.AddWithValue("$disk", reading.Disk);
                insert.Parameters.AddWithValue("$attr", reading.AttributeId);
                insert.Parameters.AddWithValue("$raw", reading.RawValue);
                insert.Parameters.AddWithValue("$at", now);
                insert.ExecuteNonQuery();
            }

            // Rétention 90 jours.
            using var prune = _connection.CreateCommand();
            prune.CommandText = "DELETE FROM smart_snapshots WHERE recorded_at < $limit";
            prune.Parameters.AddWithValue("$limit", now - 90L * 24 * 3600);
            prune.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Analyse de tendance SMART impossible", ex);
        }

        if (warnings.Count > 0)
            Infrastructure.AppLog.Warn($"SMART: {warnings.Count} attribut(s) en dégradation détecté(s)");

        return warnings;
    }

    /// <summary>
    /// Table SMART ATA : entrées de 12 octets à partir de l'offset 2
    /// (id, flags×2, value, worst, raw×6, réservé).
    /// </summary>
    internal static IReadOnlyList<(int Id, long Raw)> ParseVendorSpecific(byte[] data)
    {
        var result = new List<(int, long)>();
        for (var offset = 2; offset + 12 <= data.Length; offset += 12)
        {
            var id = data[offset];
            if (id == 0)
                continue;

            long raw = 0;
            for (var i = 0; i < 6; i++)
                raw |= (long)data[offset + 5 + i] << (8 * i);

            result.Add((id, raw));
        }
        return result;
    }

    private static string ShortenInstanceName(string instanceName)
    {
        // "IDE\DiskSamsung_SSD_870_EVO...\4&2f2a..._0" → "Samsung SSD 870 EVO"
        var value = instanceName;
        var parts = value.Split('\\');
        if (parts.Length >= 2)
            value = parts[1];

        value = value.Replace("Disk", string.Empty).Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(value) ? instanceName : value;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
