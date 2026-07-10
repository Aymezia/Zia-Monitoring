using System.Diagnostics;
using System.Management;

namespace ZiaMonitoring_App.Application;

public sealed record RestorePointsUsage(double UsedGb, int ShadowCount)
{
    public string Label => ShadowCount == 0
        ? "Aucun point de restauration présent."
        : $"{ShadowCount} point(s) de restauration occupant {UsedGb:F1} Go.";
}

public sealed record CleanupTarget(string Id, string Name, string Description, double SizeMb, bool Available)
{
    public string SizeLabel => SizeMb >= 1024 ? $"{SizeMb / 1024:F1} Go" : $"{SizeMb:F0} Mo";
}

public sealed record OldInstallerInfo(string Path, double SizeMb, DateTime LastModified)
{
    public string Label => $"{System.IO.Path.GetFileName(Path)} — {(SizeMb >= 1024 ? $"{SizeMb / 1024:F1} Go" : $"{SizeMb:F0} Mo")}, {LastModified:dd/MM/yyyy}";
}

/// <summary>
/// Nettoyages d'espace disque avancés, au-delà des fichiers temporaires
/// classiques : mise en veille prolongée (hiberfil.sys), composants Windows
/// obsolètes (WinSxS), cache Windows Update, Windows.old, et repérage des
/// vieux installeurs du dossier Téléchargements. Les opérations lourdes
/// (WinSxS, hiberfil) délèguent aux outils Windows officiels, seuls capables
/// de le faire sans casser le système.
/// </summary>
public sealed class AdvancedCleanupService
{
    private const int OldInstallerMonths = 6;
    private static readonly string[] InstallerExtensions = [".exe", ".msi"];

    public IReadOnlyList<CleanupTarget> Scan()
    {
        return
        [
            ScanHiberfil(),
            ScanSoftwareDistribution(),
            ScanWindowsOld(),
        ];
    }

    private static CleanupTarget ScanHiberfil()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System)[..3], "hiberfil.sys");
        var sizeMb = SafeFileSizeMb(path);
        return new CleanupTarget("hiberfil", "Fichier de mise en veille prolongée (hiberfil.sys)",
            "Si vous n'utilisez jamais la veille prolongée, ce fichier (souvent plusieurs Go) est récupérable. Désactive aussi le démarrage rapide.",
            sizeMb, sizeMb > 0);
    }

    private static CleanupTarget ScanSoftwareDistribution()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        var sizeMb = SafeDirectorySizeMb(path);
        return new CleanupTarget("softwaredistribution", "Cache Windows Update",
            "Fichiers d'installation de mises à jour déjà appliquées. Se purge mal tout seul ; Windows les retélécharge si besoin.",
            sizeMb, sizeMb > 1);
    }

    private static CleanupTarget ScanWindowsOld()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..3], "Windows.old");
        var sizeMb = Directory.Exists(path) ? SafeDirectorySizeMb(path) : 0;
        return new CleanupTarget("windowsold", "Ancienne installation Windows (Windows.old)",
            "Laissée après une grosse mise à jour de Windows, pour permettre un retour arrière pendant 10 jours. Passé ce délai, elle ne sert plus à rien (10-30 Go).",
            sizeMb, Directory.Exists(path));
    }

    public (bool Success, string Message) Clean(string id)
    {
        try
        {
            return id switch
            {
                "hiberfil" => DisableHibernation(),
                "softwaredistribution" => CleanSoftwareDistribution(),
                "windowsold" => CleanWindowsOld(),
                _ => (false, "Cible inconnue.")
            };
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Nettoyage avancé '{id}' impossible", ex);
            return (false, ex.Message);
        }
    }

    private static (bool, string) DisableHibernation()
    {
        var (code, _, err) = RunProcess("powercfg.exe", "/hibernate off");
        return code == 0
            ? (true, "Mise en veille prolongée désactivée : hiberfil.sys supprimé par Windows.")
            : (false, $"Échec : {err}");
    }

    private static (bool, string) CleanSoftwareDistribution()
    {
        RunProcess("net.exe", "stop wuauserv");
        RunProcess("net.exe", "stop bits");

        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
        var freed = SafeDirectorySizeMb(path);
        var errors = 0;
        foreach (var entry in SafeEnumerate(path))
        {
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            catch { errors++; }
        }

        RunProcess("net.exe", "start wuauserv");
        RunProcess("net.exe", "start bits");

        return (true, errors == 0
            ? $"Cache Windows Update vidé : ~{freed:F0} Mo libérés."
            : $"Cache partiellement vidé (~{freed:F0} Mo), {errors} élément(s) verrouillé(s) ignoré(s).");
    }

    private static (bool, string) CleanWindowsOld()
    {
        // cleanmgr avec le profil dédié est le seul moyen sûr de retirer
        // Windows.old (fichiers protégés par des ACL système).
        RunProcess("cmd.exe", "/c rd /s /q %SystemDrive%\\Windows.old", timeoutMs: 60000);
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..3], "Windows.old");
        return Directory.Exists(path)
            ? (false, "Suppression incomplète : lancez « Nettoyage de disque » Windows (option « Installations Windows précédentes ») pour les fichiers protégés restants.")
            : (true, "Windows.old supprimé.");
    }

    /// <summary>Vieux installeurs (.exe/.msi de plus de 6 mois) du dossier Téléchargements.</summary>
    public IReadOnlyList<OldInstallerInfo> ScanOldInstallers()
    {
        var downloads = GetDownloadsFolder();
        if (downloads is null || !Directory.Exists(downloads))
            return [];

        var results = new List<OldInstallerInfo>();
        var cutoff = DateTime.Now.AddMonths(-OldInstallerMonths);

        foreach (var file in SafeEnumerate(downloads))
        {
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || !IsInstaller(info.Extension) || info.LastWriteTime > cutoff)
                    continue;

                results.Add(new OldInstallerInfo(file, info.Length / 1024.0 / 1024.0, info.LastWriteTime));
            }
            catch { }
        }

        return results.OrderByDescending(r => r.SizeMb).ToList();
    }

    public (bool Success, string Message) DeleteInstaller(string path)
    {
        try
        {
            File.Delete(path);
            return (true, $"{Path.GetFileName(path)} supprimé.");
        }
        catch (Exception ex)
        {
            return (false, $"Suppression impossible : {ex.Message}");
        }
    }

    /// <summary>
    /// Espace occupé par les points de restauration, via WMI (Win32_ShadowStorage /
    /// Win32_ShadowCopy) plutôt que le texte localisé de vssadmin.
    /// </summary>
    public static RestorePointsUsage GetRestorePointsUsage()
    {
        double usedBytes = 0;
        var count = 0;
        try
        {
            using (var storage = new ManagementObjectSearcher("SELECT UsedSpace FROM Win32_ShadowStorage"))
            {
                foreach (var obj in storage.Get().Cast<ManagementObject>())
                    usedBytes += obj["UsedSpace"] is ulong used ? used : 0;
            }

            using var shadows = new ManagementObjectSearcher("SELECT ID FROM Win32_ShadowCopy");
            count = shadows.Get().Count;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'espace des points de restauration impossible", ex);
        }

        return new RestorePointsUsage(usedBytes / 1024.0 / 1024.0 / 1024.0, count);
    }

    /// <summary>Supprime tous les points de restauration sauf le plus récent (conserve un filet de sécurité).</summary>
    public (bool Success, string Message) PurgeOldRestorePoints()
    {
        try
        {
            var shadows = new List<(string Id, DateTime Created)>();
            using (var searcher = new ManagementObjectSearcher("SELECT ID, InstallDate FROM Win32_ShadowCopy"))
            {
                foreach (var obj in searcher.Get().Cast<ManagementObject>())
                {
                    var id = obj["ID"]?.ToString();
                    if (string.IsNullOrEmpty(id))
                        continue;
                    var created = obj["InstallDate"] is string dmtf ? ManagementDateTimeConverter.ToDateTime(dmtf) : DateTime.MinValue;
                    shadows.Add((id, created));
                }
            }

            if (shadows.Count <= 1)
                return (true, "Rien à purger : un seul point de restauration (conservé comme filet de sécurité).");

            var toDelete = shadows.OrderByDescending(s => s.Created).Skip(1).ToList();
            var deleted = 0;
            foreach (var (id, _) in toDelete)
            {
                try
                {
                    using var shadow = new ManagementObject($"Win32_ShadowCopy.ID=\"{id}\"");
                    shadow.Delete();
                    deleted++;
                }
                catch (Exception ex)
                {
                    Infrastructure.AppLog.Warn($"Suppression du point de restauration {id} impossible", ex);
                }
            }

            return (true, $"{deleted} ancien(s) point(s) de restauration supprimé(s), le plus récent conservé.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Purge des points de restauration impossible", ex);
            return (false, ex.Message);
        }
    }

    internal static bool IsInstaller(string extension) =>
        InstallerExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static string? GetDownloadsFolder()
    {
        // Pas de SpecialFolder pour Téléchargements avant .NET moderne ; le
        // chemin conventionnel sous le profil utilisateur couvre la quasi-totalité des cas.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "Downloads");
    }

    private static IEnumerable<string> SafeEnumerate(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.EnumerateFileSystemEntries(path) : [];
        }
        catch
        {
            return [];
        }
    }

    private static double SafeFileSizeMb(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length / 1024.0 / 1024.0 : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double SafeDirectorySizeMb(string path)
    {
        long total = 0;
        try
        {
            if (!Directory.Exists(path))
                return 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Take(50000))
            {
                try { total += new FileInfo(file).Length; }
                catch { }
            }
        }
        catch { }
        return total / 1024.0 / 1024.0;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, int timeoutMs = 15000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de démarrer {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return (process.ExitCode, output, error);
    }
}
