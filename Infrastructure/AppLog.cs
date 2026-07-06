using System.Collections.Concurrent;

namespace ZiaMonitoring_App.Infrastructure;

/// <summary>
/// Logger applicatif partagé : fichier unique avec rotation par taille et
/// déduplication des messages répétitifs (les collecteurs tournent chaque seconde).
/// Ne lève jamais d'exception vers l'appelant.
/// </summary>
public static class AppLog
{
    private const long MaxLogSizeBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan DuplicateSuppressionWindow = TimeSpan.FromMinutes(30);

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, DateTime> RecentMessages = new();
    private static readonly string LogFile;

    static AppLog()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring", "logs");
        try { Directory.CreateDirectory(dir); } catch { }
        LogFile = Path.Combine(dir, "app.log");
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            // Un collecteur qui échoue le fait souvent à chaque tick : on ne
            // journalise le même message qu'une fois par fenêtre de suppression.
            var key = $"{level}|{message}|{exception?.GetType().Name}";
            var now = DateTime.UtcNow;
            if (RecentMessages.TryGetValue(key, out var lastWrite)
                && now - lastWrite < DuplicateSuppressionWindow)
            {
                return;
            }

            RecentMessages[key] = now;
            if (RecentMessages.Count > 256)
                PruneRecentMessages(now);

            var line = exception is null
                ? $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}"
                : $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message} — {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}";

            lock (Gate)
            {
                RotateIfNeeded();
                File.AppendAllText(LogFile, line);
            }
        }
        catch
        {
            // Le logging ne doit jamais faire tomber l'application.
        }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(LogFile);
        if (!info.Exists || info.Length < MaxLogSizeBytes)
            return;

        var archive = Path.ChangeExtension(LogFile, ".old.log");
        File.Delete(archive);
        File.Move(LogFile, archive);
    }

    private static void PruneRecentMessages(DateTime now)
    {
        foreach (var entry in RecentMessages)
        {
            if (now - entry.Value >= DuplicateSuppressionWindow)
                RecentMessages.TryRemove(entry.Key, out _);
        }
    }
}
