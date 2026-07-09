using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace ZiaMonitoring_App.Application;

public sealed record DiskMediaTypeInfo(string Name, string MediaTypeLabel, bool IsUnspecified);

/// <summary>
/// Vérifie que l'optimiseur de disques Windows (Optimize-Drives /
/// ScheduledDefrag) traite chaque disque correctement : les SSD doivent
/// recevoir un TRIM/ReTrim, pas un défragmentation classique — Windows base
/// cette décision sur le MediaType exposé par MSFT_PhysicalDisk. Un disque
/// "Non spécifié" est le seul cas où Windows ne peut pas garantir le bon
/// traitement (le firmware ne déclare pas correctement sa nature).
/// </summary>
public static class DiskOptimizationAuditService
{
    public static IReadOnlyList<DiskMediaTypeInfo> GetDiskMediaTypes()
    {
        var result = new List<DiskMediaTypeInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage", "SELECT FriendlyName, MediaType FROM MSFT_PhysicalDisk");

            foreach (var disk in searcher.Get().Cast<ManagementObject>())
            {
                var name = disk["FriendlyName"]?.ToString() ?? "Disque inconnu";
                var mediaType = disk["MediaType"] is ushort mt ? mt : (ushort)0;
                result.Add(new DiskMediaTypeInfo(name, MediaTypeLabel(mediaType), mediaType == 0));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture WMI des disques physiques impossible", ex);
        }

        return result;
    }

    internal static string MediaTypeLabel(ushort mediaType) => mediaType switch
    {
        3 => "HDD",
        4 => "SSD",
        5 => "SCM",
        _ => "Non spécifié"
    };

    /// <summary>Vrai si la tâche d'optimisation planifiée Windows (TRIM + défrag HDD) est active.</summary>
    public static bool IsScheduledOptimizationEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", "/Query /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /XML")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer schtasks.");
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(8000);
            return IsEnabledFromTaskXml(output);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de la tâche d'optimisation planifiée impossible", ex);
            return true; // par prudence, ne pas prétendre à tort qu'elle est désactivée.
        }
    }

    /// <summary>
    /// Même technique que DebloatService.ParseTaskEnabledFromXml : /XML expose
    /// &lt;Enabled&gt; de façon structurelle, indépendante de la langue d'affichage
    /// (contrairement à /FO CSV, qui renvoie un statut localisé).
    /// </summary>
    internal static bool IsEnabledFromTaskXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return true; // tâche introuvable : ne pas prétendre à tort qu'elle est désactivée.

        var settingsStart = xml.IndexOf("<Settings>", StringComparison.OrdinalIgnoreCase);
        var settingsEnd = xml.IndexOf("</Settings>", StringComparison.OrdinalIgnoreCase);
        if (settingsStart < 0 || settingsEnd < 0 || settingsEnd <= settingsStart)
            return true;

        var settingsBlock = xml[settingsStart..settingsEnd];
        var match = Regex.Match(settingsBlock, @"<Enabled>(true|false)</Enabled>", RegexOptions.IgnoreCase);

        return !match.Success || match.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
