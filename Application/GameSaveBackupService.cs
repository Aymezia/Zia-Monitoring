using System.IO.Compression;

namespace ZiaMonitoring_App.Application;

public sealed record SaveBackupResult(string ZipPath, int FileCount, long TotalBytes, IReadOnlyList<string> Warnings)
{
    public string Summary => $"{FileCount} fichier(s), {TotalBytes / 1024.0 / 1024.0:F1} Mo → {ZipPath}";
}

public sealed record RestoreTestResult(bool Success, string Message);

/// <summary>
/// Sauvegarde en zip les dossiers de sauvegardes de jeux génériques les
/// plus répandus : Documents\My Games (convention historique très utilisée)
/// et %USERPROFILE%\Saved Games (dossier officiel Windows, FOLDERID_SavedGames,
/// utilisé par de nombreux jeux modernes). On ne peut pas cibler les chemins
/// spécifiques à chaque jeu sans une base de correspondance fiable que nous
/// n'avons pas — ces deux dossiers couvrent la majorité des cas réels.
/// </summary>
public sealed class GameSaveBackupService
{
    private const int MaxFilesPerBackup = 20_000;
    private const int KeepBackupCount = 7;

    private DateTime _lastScheduledRunDate = DateTime.MinValue;

    /// <summary>Vrai une fois par jour à l'heure planifiée, si l'option est activée.</summary>
    public bool IsScheduledRunDue(bool enabled, TimeSpan scheduledTime)
    {
        if (!enabled || _lastScheduledRunDate == DateTime.Today)
            return false;

        if (DateTime.Now.TimeOfDay < scheduledTime)
            return false;

        _lastScheduledRunDate = DateTime.Today;
        return true;
    }

    public SaveBackupResult BackupNow(string? destinationDirectory = null)
    {
        var destDir = destinationDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring", "SaveBackups");
        Directory.CreateDirectory(destDir);

        var zipPath = Path.Combine(destDir, $"saves-{DateTime.Now:yyyy-MM-dd_HHmm}.zip");
        var warnings = new List<string>();
        var fileCount = 0;
        long totalBytes = 0;

        try
        {
            using var zipStream = new FileStream(zipPath, FileMode.Create);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

            foreach (var folder in GetCandidateFolders())
            {
                if (!Directory.Exists(folder.Path))
                    continue;

                foreach (var file in SafeEnumerateFiles(folder.Path))
                {
                    try
                    {
                        var relative = Path.Combine(folder.Label, Path.GetRelativePath(folder.Path, file));
                        archive.CreateEntryFromFile(file, relative, CompressionLevel.Fastest);
                        totalBytes += new FileInfo(file).Length;
                        fileCount++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Ignoré : {Path.GetFileName(file)} ({ex.Message})");
                    }

                    if (fileCount >= MaxFilesPerBackup)
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde des saves de jeux impossible", ex);
            warnings.Add($"Échec global : {ex.Message}");
        }

        PruneOldBackups(destDir);
        return new SaveBackupResult(zipPath, fileCount, totalBytes, warnings);
    }

    /// <summary>
    /// Extrait la sauvegarde la plus récente dans un dossier temporaire pour
    /// vérifier qu'elle n'est pas corrompue, avant d'en avoir réellement
    /// besoin. Le dossier temporaire est toujours supprimé après coup —
    /// c'est un test d'intégrité, pas une vraie restauration.
    /// </summary>
    public RestoreTestResult TestRestoreLatestBackup(string? backupDirectory = null)
    {
        var destDir = backupDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring", "SaveBackups");

        FileInfo? latest;
        try
        {
            latest = new DirectoryInfo(destDir).GetFiles("saves-*.zip").OrderByDescending(f => f.CreationTimeUtc).FirstOrDefault();
        }
        catch (Exception ex)
        {
            return new RestoreTestResult(false, $"Impossible d'accéder au dossier de sauvegardes : {ex.Message}");
        }

        if (latest is null)
            return new RestoreTestResult(false, "Aucune sauvegarde trouvée — lancez d'abord une sauvegarde.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ZiaMonitoringRestoreTest-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            var count = 0;
            using (var archive = ZipFile.OpenRead(latest.FullName))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue; // entrée de dossier, rien à extraire.

                    var destPath = Path.Combine(tempDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    entry.ExtractToFile(destPath, overwrite: true);
                    count++;
                }
            }

            return new RestoreTestResult(true,
                $"Sauvegarde '{latest.Name}' restaurée avec succès dans un dossier temporaire : {count} fichier(s) extrait(s) sans erreur, aucune corruption détectée.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Test de restauration de '{latest.Name}' en échec", ex);
            return new RestoreTestResult(false, $"La sauvegarde '{latest.Name}' semble corrompue : {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("Nettoyage du dossier de test de restauration impossible", ex);
            }
        }
    }

    internal static IEnumerable<(string Label, string Path)> GetCandidateFolders()
    {
        yield return ("MyGames", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games"));
        yield return ("SavedGames", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games"));
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Énumération de '{root}' impossible", ex);
            yield break;
        }

        foreach (var file in files)
            yield return file;
    }

    /// <summary>Conserve les dernières sauvegardes, supprime le reste.</summary>
    private static void PruneOldBackups(string destDir)
    {
        try
        {
            var old = new DirectoryInfo(destDir)
                .GetFiles("saves-*.zip")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(KeepBackupCount);

            foreach (var file in old)
                file.Delete();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Purge des anciennes sauvegardes impossible", ex);
        }
    }
}
