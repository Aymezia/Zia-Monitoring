using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record PendingRebootStatus(bool RebootRequired, IReadOnlyList<string> Reasons, IReadOnlyList<string> SoftIndicators, TimeSpan Uptime)
{
    public string Label => RebootRequired
        ? $"Redémarrage requis depuis {FormatUptime(Uptime)} : {string.Join(", ", Reasons)}."
        : SoftIndicators.Count > 0
            ? $"Pas de redémarrage impératif, mais des opérations sont en attente ({string.Join(", ", SoftIndicators)})."
            : "Aucun redémarrage en attente.";

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays} j {uptime.Hours} h"
        : $"{(int)uptime.TotalHours} h {uptime.Minutes} min";
}

/// <summary>
/// Détecte un redémarrage Windows en attente via les indicateurs officiels du
/// registre. Distingue les signaux forts (Component Based Servicing, Windows
/// Update — un vrai reboot est requis) des signaux faibles
/// (PendingFileRenameOperations, très courant après une simple installation
/// et rarement bloquant) pour ne pas alerter à tort. Croise avec l'uptime :
/// une machine qui tourne depuis des jours avec un reboot en attente accumule
/// des mises à jour non appliquées.
/// </summary>
public sealed class PendingRebootService
{
    public PendingRebootStatus GetStatus()
    {
        var cbs = KeyExists(Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
        var windowsUpdate = KeyExists(Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
        var wuServicesPending = HasSubKeys(Registry.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Services\Pending");
        var pendingRename = ValueExists(Registry.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations");

        return Evaluate(cbs, windowsUpdate, wuServicesPending, pendingRename,
            TimeSpan.FromMilliseconds(Environment.TickCount64));
    }

    internal static PendingRebootStatus Evaluate(bool cbs, bool windowsUpdate, bool wuServicesPending, bool pendingRename, TimeSpan uptime)
    {
        var reasons = new List<string>();
        if (cbs) reasons.Add("maintenance de composants Windows");
        if (windowsUpdate) reasons.Add("Windows Update");
        if (wuServicesPending) reasons.Add("services Windows Update en attente");

        var soft = new List<string>();
        if (pendingRename) soft.Add("renommage de fichiers au prochain démarrage");

        return new PendingRebootStatus(reasons.Count > 0, reasons, soft, uptime);
    }

    private static bool KeyExists(RegistryKey hive, string subKey)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey, writable: false);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSubKeys(RegistryKey hive, string subKey)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey, writable: false);
            return key is not null && key.GetSubKeyNames().Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool ValueExists(RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using var key = hive.OpenSubKey(subKey, writable: false);
            return key?.GetValue(valueName) is not null;
        }
        catch
        {
            return false;
        }
    }
}
