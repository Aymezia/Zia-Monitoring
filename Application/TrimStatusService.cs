using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ZiaMonitoring_App.Application;

public sealed record TrimStatusInfo(bool NtfsTrimEnabled, bool RefsTrimEnabled)
{
    public string Label => NtfsTrimEnabled
        ? "TRIM activé (NTFS) : Windows notifie le SSD des blocs libérés — durée de vie et performances préservées."
        : "TRIM désactivé (NTFS) : les SSD ne sont plus notifiés des blocs libérés, ce qui dégrade leurs performances et leur durée de vie dans le temps.";
}

/// <summary>
/// Statut TRIM (DisableDeleteNotify) via fsutil — un réglage global souvent
/// modifié par erreur (certains outils d'optimisation "gaming" le désactivent
/// à tort en pensant accélérer le système, l'effet réel est l'inverse sur SSD).
/// Le nom du paramètre "DisableDeleteNotify" n'est pas localisé par fsutil,
/// contrairement au texte descriptif entre parenthèses : le parsing reste
/// donc fiable quelle que soit la langue de Windows.
/// </summary>
public static class TrimStatusService
{
    public static TrimStatusInfo? GetStatus()
    {
        try
        {
            var psi = new ProcessStartInfo("fsutil.exe", "behavior query DisableDeleteNotify")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer fsutil.");
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(8000);
            return ParseOutput(output);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du statut TRIM impossible", ex);
            return null;
        }
    }

    internal static TrimStatusInfo? ParseOutput(string output)
    {
        var ntfs = Regex.Match(output, @"NTFS\s+DisableDeleteNotify\s*=\s*(\d)", RegexOptions.IgnoreCase);
        var refs = Regex.Match(output, @"ReFS\s+DisableDeleteNotify\s*=\s*(\d)", RegexOptions.IgnoreCase);

        if (!ntfs.Success && !refs.Success)
            return null;

        // DisableDeleteNotify = 0 signifie que TRIM est AUTORISÉ (le nom du
        // paramètre est une double négation : "ne pas désactiver la notification").
        var ntfsEnabled = !ntfs.Success || ntfs.Groups[1].Value == "0";
        var refsEnabled = !refs.Success || refs.Groups[1].Value == "0";
        return new TrimStatusInfo(ntfsEnabled, refsEnabled);
    }
}
