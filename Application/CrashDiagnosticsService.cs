using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record AppCrashInfo(DateTime OccurredAt, string AppName, string FaultingModule, string ExceptionCode, bool IsKnownGame);

public sealed record AppCrashGroup(string AppName, int Count, DateTime LastAt, string MostCommonModule, bool IsKnownGame)
{
    public string Label =>
        $"{AppName}{(IsKnownGame ? " 🎮" : "")} — {Count} crash(s), dernier le {LastAt:dd/MM HH:mm} (module : {MostCommonModule})";
}

public sealed record WheaSummary(int CorrectedErrorCount, DateTime? LastAt)
{
    public string Label => CorrectedErrorCount == 0
        ? "Aucune erreur matérielle corrigée (WHEA) sur 30 jours — bon signe pour la stabilité RAM/CPU."
        : $"{CorrectedErrorCount} erreur(s) matérielle(s) corrigée(s) sur 30 jours (dernière : {LastAt:dd/MM HH:mm}). "
          + "Signal précoce d'un overclock RAM/CPU instable : envisagez de baisser XMP/EXPO ou l'OC.";
}

public sealed record BsodInfo(DateTime OccurredAt, string BugcheckText)
{
    public string Label => $"Dernier écran bleu : {OccurredAt:dd/MM/yyyy HH:mm} — {BugcheckText}";
}

public sealed record ServiceCrashLoopWarning(string ServiceName, int Count, DateTime LastAt)
{
    public string Label => $"{ServiceName} : {Count} arrêt(s) inattendu(s) en moins d'une heure (dernier {LastAt:dd/MM HH:mm}) — boucle de redémarrage probable.";
}

/// <summary>
/// Diagnostics de stabilité en lecture seule depuis les journaux Windows :
/// crashs d'applications (Event 1000), erreurs matérielles corrigées WHEA
/// (Event 19), dernier BSOD (Event 1001 WER), verdict du diagnostic mémoire
/// Windows et services en boucle de redémarrage (Event 7031/7034). Aucune
/// trace à activer, Windows journalise déjà tout.
/// </summary>
public sealed class CrashDiagnosticsService
{
    private readonly string _stateFile;
    private DateTime? _lastNotifiedBsod;

    public CrashDiagnosticsService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "crash-diagnostics-state.json");
        LoadState();
    }

    public IReadOnlyList<AppCrashGroup> GetRecentAppCrashes(int days = 30, int maxEvents = 200)
    {
        var crashes = new List<AppCrashInfo>();

        try
        {
            var query = new EventLogQuery("Application", PathType.LogName,
                $"*[System[Provider[@Name='Application Error'] and (EventID=1000) and TimeCreated[timediff(@SystemTime) <= {days * 86400000L}]]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while (crashes.Count < maxEvents && (record = reader.ReadEvent()) is not null)
            {
                using (record)
                {
                    var appName = ReadProperty(record, 0) ?? "Inconnu";
                    var module = ReadProperty(record, 3) ?? "inconnu";
                    var code = ReadProperty(record, 6) ?? "?";
                    var isGame = ActiveGameDetector.IsKnownGameProcess(Path.GetFileNameWithoutExtension(appName));
                    crashes.Add(new AppCrashInfo(record.TimeCreated ?? DateTime.MinValue, appName, module, code, isGame));
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des crashs d'applications (journal Windows) impossible", ex);
        }

        return GroupCrashes(crashes);
    }

    /// <summary>Regroupement pur, séparé de la lecture du journal pour être testable.</summary>
    internal static IReadOnlyList<AppCrashGroup> GroupCrashes(IReadOnlyList<AppCrashInfo> crashes)
    {
        return crashes
            .GroupBy(c => c.AppName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new AppCrashGroup(
                g.Key,
                g.Count(),
                g.Max(c => c.OccurredAt),
                g.GroupBy(c => c.FaultingModule, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(m => m.Count())
                    .First().Key,
                g.Any(c => c.IsKnownGame)))
            .OrderByDescending(g => g.IsKnownGame)
            .ThenByDescending(g => g.Count)
            .ToList();
    }

    public WheaSummary GetWheaSummary(int days = 30)
    {
        var count = 0;
        DateTime? last = null;

        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (EventID=19) and TimeCreated[timediff(@SystemTime) <= {days * 86400000L}]]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) is not null)
            {
                using (record)
                {
                    count++;
                    if (record.TimeCreated is { } at && (last is null || at > last))
                        last = at;
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des erreurs WHEA impossible", ex);
        }

        return new WheaSummary(count, last);
    }

    public BsodInfo? GetLastBsod()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and (EventID=1001)]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record is null)
                return null;

            var bugcheck = ReadProperty(record, 0) ?? "code inconnu";
            return new BsodInfo(record.TimeCreated ?? DateTime.MinValue, bugcheck);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du dernier BSOD impossible", ex);
            return null;
        }
    }

    public IReadOnlyList<ServiceCrashLoopWarning> GetServiceCrashLoops(int windowMinutes = 60, int minOccurrences = 3)
    {
        var events = new List<(string Service, DateTime At)>();

        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='Service Control Manager'] and (EventID=7031 or EventID=7034) and TimeCreated[timediff(@SystemTime) <= {windowMinutes * 60000L}]]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while ((record = reader.ReadEvent()) is not null)
            {
                using (record)
                {
                    var service = ReadProperty(record, 0) ?? "Service inconnu";
                    events.Add((service, record.TimeCreated ?? DateTime.MinValue));
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des arrêts de services (journal Windows) impossible", ex);
        }

        return DetectCrashLoops(events, minOccurrences);
    }

    /// <summary>Regroupement pur, séparé de la lecture du journal pour être testable.</summary>
    internal static IReadOnlyList<ServiceCrashLoopWarning> DetectCrashLoops(
        IReadOnlyList<(string Service, DateTime At)> events, int minOccurrences)
    {
        return events
            .GroupBy(e => e.Service, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= minOccurrences)
            .Select(g => new ServiceCrashLoopWarning(g.Key, g.Count(), g.Max(e => e.At)))
            .OrderByDescending(w => w.Count)
            .ToList();
    }

    /// <summary>
    /// Retourne le dernier BSOD s'il est postérieur au dernier notifié (état
    /// persisté), sinon null. Permet une seule notification par BSOD, y
    /// compris à travers un redémarrage de l'application.
    /// </summary>
    public BsodInfo? CheckForNewBsodSinceLastNotified()
    {
        var bsod = GetLastBsod();
        if (bsod is null || !IsNewBsod(bsod.OccurredAt, _lastNotifiedBsod))
            return null;

        _lastNotifiedBsod = bsod.OccurredAt;
        SaveState();
        return bsod;
    }

    internal static bool IsNewBsod(DateTime bsodOccurredAt, DateTime? lastNotified) =>
        lastNotified is null || bsodOccurredAt > lastNotified;

    private void LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
                _lastNotifiedBsod = JsonSerializer.Deserialize<DateTime?>(File.ReadAllText(_stateFile));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'état de diagnostic de crash impossible", ex);
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_lastNotifiedBsod));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde de l'état de diagnostic de crash impossible", ex);
        }
    }

    /// <summary>Lance mdsched.exe (propose un redémarrage immédiat ou différé — le test tourne avant le boot).</summary>
    public static (bool Success, string Message) LaunchMemoryDiagnostic()
    {
        try
        {
            Process.Start(new ProcessStartInfo("mdsched.exe") { UseShellExecute = true });
            return (true, "Outil de diagnostic mémoire lancé — le test s'exécute au prochain redémarrage, verdict lisible ici ensuite.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lancement du diagnostic mémoire impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Verdict du dernier diagnostic mémoire (journal MemoryDiagnostics-Results).</summary>
    public string? GetLastMemoryDiagnosticResult()
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']]]")
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record is null)
                return null;

            var description = record.FormatDescription();
            return string.IsNullOrWhiteSpace(description)
                ? null
                : $"{record.TimeCreated:dd/MM/yyyy HH:mm} — {description.Trim()}";
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du verdict du diagnostic mémoire impossible", ex);
            return null;
        }
    }

    private static string? ReadProperty(EventRecord record, int index)
    {
        try
        {
            return record.Properties.Count > index ? record.Properties[index].Value?.ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}
