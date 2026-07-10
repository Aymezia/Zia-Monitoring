using System.Diagnostics;
using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record ContextMenuHandler(string Root, string Name, string DisplayName, string Clsid)
{
    public string Label => $"{DisplayName} — {Root}";
}

/// <summary>
/// Petits remèdes système du quotidien : redémarrer l'Explorateur (le fix
/// universel des bugs de barre des tâches), réparer le cache d'icônes/
/// miniatures corrompu, et faire l'inventaire des gestionnaires de menu
/// contextuel tiers (les entrées du clic droit qui alourdissent
/// l'Explorateur). L'inventaire est en lecture seule : retirer un
/// gestionnaire se fait proprement en désinstallant le logiciel concerné —
/// éditer directement les extensions du shell HKCR peut casser l'Explorateur
/// et n'est pas garanti réversible, on ne le propose donc pas ici.
/// </summary>
public static class SystemHygieneService
{
    private static readonly (string RootKey, string Label)[] ContextMenuRoots =
    [
        (@"*\shellex\ContextMenuHandlers", "Fichiers"),
        (@"Directory\shellex\ContextMenuHandlers", "Dossiers"),
        (@"Directory\Background\shellex\ContextMenuHandlers", "Arrière-plan du bureau"),
    ];

    // Gestionnaires livrés avec Windows : exclus de l'inventaire pour ne
    // montrer que ce que les logiciels tiers ont ajouté.
    private static readonly string[] BuiltInHandlerNames =
    [
        "New", "WorkFolders", "Sharing", "EPP", "Library Location",
        "BriefcaseMenu", "Open With", "Open With EncryptionMenu",
    ];

    public static (bool Success, string Message) RestartExplorer()
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                using (process)
                    process.Kill();
            }

            Thread.Sleep(800);
            if (Process.GetProcessesByName("explorer").Length == 0)
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });

            return (true, "Explorateur redémarré.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Redémarrage de l'Explorateur impossible", ex);
            return (false, ex.Message);
        }
    }

    public static (bool Success, string Message) RepairIconCache()
    {
        try
        {
            var explorerCacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Explorer");

            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                using (process)
                    process.Kill();
            }
            Thread.Sleep(800);

            var deleted = 0;
            if (Directory.Exists(explorerCacheDir))
            {
                foreach (var file in Directory.EnumerateFiles(explorerCacheDir, "iconcache_*.db")
                             .Concat(Directory.EnumerateFiles(explorerCacheDir, "thumbcache_*.db")))
                {
                    try { File.Delete(file); deleted++; }
                    catch { }
                }
            }

            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            return (true, $"Cache d'icônes réparé : {deleted} fichier(s) supprimé(s), Explorateur redémarré.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Réparation du cache d'icônes impossible", ex);
            return (false, ex.Message);
        }
    }

    public static IReadOnlyList<ContextMenuHandler> ScanContextMenuHandlers()
    {
        var result = new List<ContextMenuHandler>();
        foreach (var (rootKey, label) in ContextMenuRoots)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(rootKey, writable: false);
                if (key is null)
                    continue;

                foreach (var name in key.GetSubKeyNames())
                {
                    if (IsBuiltIn(name))
                        continue;

                    using var handler = key.OpenSubKey(name, writable: false);
                    var clsid = handler?.GetValue(null)?.ToString() ?? string.Empty;
                    result.Add(new ContextMenuHandler(label, name, PrettifyName(name), clsid));
                }
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn($"Lecture des gestionnaires de menu contextuel ({rootKey}) impossible", ex);
            }
        }

        return result.OrderBy(h => h.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Ouvre les Options d'indexation Windows (pour exclure les dossiers de jeux).</summary>
    public static (bool Success, string Message) OpenIndexingOptions()
    {
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "srchadmin.dll") { UseShellExecute = true });
            return (true, "Options d'indexation ouvertes : retirez les dossiers de jeux de la liste des emplacements indexés.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    internal static bool IsBuiltIn(string handlerName) =>
        BuiltInHandlerNames.Contains(handlerName, StringComparer.OrdinalIgnoreCase);

    internal static string PrettifyName(string rawName) =>
        rawName.StartsWith('{') && rawName.EndsWith('}') ? rawName : rawName.Trim();
}
