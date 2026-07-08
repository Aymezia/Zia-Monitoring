using System.Diagnostics;
using System.Management;

namespace ZiaMonitoring_App.Application;

public sealed record InstalledUpdateInfo(string HotFixId, string Description, DateTime? InstalledOn)
{
    public string Label => $"{HotFixId} — {(string.IsNullOrWhiteSpace(Description) ? "Mise à jour" : Description)}"
        + (InstalledOn is { } d ? $" (installée le {d:dd/MM/yyyy})" : "");
}

/// <summary>
/// Historique des mises à jour Windows installées (WMI Win32_QuickFixEngineering)
/// avec désinstallation d'une KB via wusa.exe — la boîte de confirmation
/// Windows reste affichée (pas de /quiet) : l'utilisateur valide lui-même.
/// </summary>
public sealed class WindowsUpdateHistoryService
{
    public IReadOnlyList<InstalledUpdateInfo> GetInstalledUpdates()
    {
        var updates = new List<InstalledUpdateInfo>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT HotFixID, Description, InstalledOn FROM Win32_QuickFixEngineering");

            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var id = item["HotFixID"]?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                DateTime? installedOn = DateTime.TryParse(item["InstalledOn"]?.ToString(), out var d) ? d : null;
                updates.Add(new InstalledUpdateInfo(id, item["Description"]?.ToString()?.Trim() ?? "", installedOn));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique Windows Update impossible", ex);
        }

        return updates
            .OrderByDescending(u => u.InstalledOn ?? DateTime.MinValue)
            .ToList();
    }

    public (bool Success, string Message) UninstallKb(string hotFixId)
    {
        var kbNumber = ExtractKbNumber(hotFixId);
        if (kbNumber is null)
            return (false, $"Identifiant KB invalide : {hotFixId}");

        try
        {
            Process.Start(new ProcessStartInfo("wusa.exe", $"/uninstall /kb:{kbNumber} /norestart")
            {
                UseShellExecute = true
            });
            return (true, $"Désinstallation de KB{kbNumber} lancée — confirmez dans la fenêtre Windows. Redémarrage requis ensuite.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Désinstallation de {hotFixId} impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>« KB5031354 » → « 5031354 » ; null si le format ne correspond pas.</summary>
    internal static string? ExtractKbNumber(string hotFixId)
    {
        hotFixId = hotFixId.Trim();
        if (!hotFixId.StartsWith("KB", StringComparison.OrdinalIgnoreCase))
            return null;

        var digits = hotFixId[2..];
        return digits.Length > 0 && digits.All(char.IsDigit) ? digits : null;
    }
}
