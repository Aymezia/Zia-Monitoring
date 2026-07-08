using System.Diagnostics;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Redémarrage intelligent : quand l'uptime dépasse le seuil configuré et
/// qu'aucun jeu ne tourne, propose (toast) un redémarrage — au plus une fois
/// par 24 h, l'horodatage étant persisté pour survivre aux relances de
/// l'app. La planification effective passe par shutdown.exe (annulable).
/// </summary>
public sealed class SmartRebootService
{
    private readonly string _stateFile;
    private DateTime? _lastProposedAt;

    public SmartRebootService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "smart-reboot.json");
        Load();
    }

    /// <summary>Retourne le message de proposition à toaster, ou null si rien à proposer.</summary>
    public string? Tick(TimeSpan uptime, bool gameActive, int thresholdDays)
    {
        var now = DateTime.UtcNow;
        if (!ShouldPropose(uptime, gameActive, _lastProposedAt, now, thresholdDays))
            return null;

        _lastProposedAt = now;
        Save();
        return $"Votre PC tourne depuis {uptime.Days} jour(s) sans redémarrage. "
             + "Un redémarrage libère la mémoire et applique les mises à jour en attente — planifiable depuis Réglages.";
    }

    /// <summary>Décision pure et testable.</summary>
    internal static bool ShouldPropose(TimeSpan uptime, bool gameActive, DateTime? lastProposedAt, DateTime now, int thresholdDays)
    {
        if (gameActive)
            return false;

        if (uptime.TotalDays < thresholdDays)
            return false;

        return lastProposedAt is null || now - lastProposedAt >= TimeSpan.FromHours(24);
    }

    /// <summary>Planifie un redémarrage à la prochaine occurrence de 03:00 via shutdown.exe.</summary>
    public static (bool Success, string Message) ScheduleNightlyReboot()
    {
        var seconds = SecondsUntilNextThreeAm(DateTime.Now);
        try
        {
            var (code, error) = RunShutdown($"/r /t {seconds} /c \"Redémarrage planifié par Zia Monitoring\"");
            return code == 0
                ? (true, $"Redémarrage planifié à 03:00 (dans {seconds / 3600.0:F1} h). Annulable à tout moment.")
                : (false, $"Planification impossible : {error} (un redémarrage est peut-être déjà planifié — annulez-le d'abord).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static (bool Success, string Message) CancelScheduledReboot()
    {
        try
        {
            var (code, error) = RunShutdown("/a");
            return code == 0
                ? (true, "Redémarrage planifié annulé.")
                : (false, $"Rien à annuler ({error}).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    internal static int SecondsUntilNextThreeAm(DateTime now)
    {
        var target = now.Date.AddHours(3);
        if (target <= now)
            target = target.AddDays(1);
        return (int)(target - now).TotalSeconds;
    }

    private static (int ExitCode, string Error) RunShutdown(string arguments)
    {
        var psi = new ProcessStartInfo("shutdown.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer shutdown.exe.");
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return (process.ExitCode, error.Length > 0 ? error : "erreur inconnue");
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_stateFile))
                _lastProposedAt = JsonSerializer.Deserialize<DateTime?>(File.ReadAllText(_stateFile));
        }
        catch
        {
            _lastProposedAt = null;
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_lastProposedAt));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Sauvegarde de l'état du redémarrage intelligent impossible", ex);
        }
    }
}
