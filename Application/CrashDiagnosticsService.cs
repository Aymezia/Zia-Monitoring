using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

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

/// <summary>
/// Diagnostics de stabilité en lecture seule depuis les journaux Windows :
/// crashs d'applications (Event 1000), erreurs matérielles corrigées WHEA
/// (Event 19), dernier BSOD (Event 1001 WER) et verdict du diagnostic
/// mémoire Windows. Aucune trace à activer, Windows journalise déjà tout.
/// </summary>
public sealed class CrashDiagnosticsService
{
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
