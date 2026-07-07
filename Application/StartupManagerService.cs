using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record StartupEntry(string Name, string Command, string Source, bool IsEnabled, string Impact)
{
    public string StatusLabel => IsEnabled ? "Activé" : "Désactivé";
    public string ToggleLabel => IsEnabled ? "Désactiver" : "Activer";
    public string DetailLabel => $"{Source} · Impact {Impact}";
}

/// <summary>
/// Gestionnaire de démarrage : liste les entrées Run (HKCU/HKLM) et permet
/// de les activer/désactiver via les clés StartupApproved — le même
/// mécanisme que le Gestionnaire des tâches, donc réversible et compatible
/// avec lui. L'impact est estimé (taille de l'exécutable + liste d'apps
/// connues pour être lourdes au boot).
/// </summary>
public sealed class StartupManagerService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static readonly string[] KnownHeavyApps =
    [
        "onedrive", "teams", "discord", "steam", "epicgameslauncher", "dropbox",
        "spotify", "skype", "slack", "icue", "razer", "wallpaper", "overwolf", "adobe"
    ];

    public IReadOnlyList<StartupEntry> GetEntries()
    {
        var entries = new List<StartupEntry>();
        CollectFromHive(Registry.CurrentUser, "Utilisateur (HKCU)", entries);
        CollectFromHive(Registry.LocalMachine, "Machine (HKLM)", entries);
        return entries.OrderByDescending(e => e.IsEnabled).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Active/désactive une entrée via StartupApproved (réversible).</summary>
    public bool SetEnabled(string name, string source, bool enabled)
    {
        try
        {
            var hive = source.Contains("HKLM", StringComparison.OrdinalIgnoreCase)
                ? Registry.LocalMachine
                : Registry.CurrentUser;

            using var key = hive.CreateSubKey(ApprovedRunKey, writable: true);
            key.SetValue(name, BuildApprovedValue(enabled), RegistryValueKind.Binary);
            Infrastructure.AppLog.Info($"Démarrage: '{name}' ({source}) → {(enabled ? "activé" : "désactivé")}");
            return true;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Bascule de l'entrée de démarrage '{name}' impossible", ex);
            return false;
        }
    }

    private static void CollectFromHive(RegistryKey hive, string sourceLabel, List<StartupEntry> entries)
    {
        try
        {
            using var runKey = hive.OpenSubKey(RunKey, writable: false);
            if (runKey is null)
                return;

            using var approvedKey = hive.OpenSubKey(ApprovedRunKey, writable: false);

            foreach (var name in runKey.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var command = runKey.GetValue(name)?.ToString() ?? string.Empty;
                var enabled = IsApprovedEnabled(approvedKey?.GetValue(name) as byte[]);
                entries.Add(new StartupEntry(name, command, sourceLabel, enabled, EstimateImpact(command)));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Lecture des entrées de démarrage ({sourceLabel}) impossible", ex);
        }
    }

    /// <summary>
    /// Convention StartupApproved (Gestionnaire des tâches) : premier octet
    /// pair = activé, impair = désactivé ; valeur absente = activé.
    /// </summary>
    internal static bool IsApprovedEnabled(byte[]? data)
        => data is null || data.Length == 0 || (data[0] & 0x01) == 0;

    internal static byte[] BuildApprovedValue(bool enabled)
    {
        var value = new byte[12];
        value[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            var filetime = DateTime.UtcNow.ToFileTimeUtc();
            for (var i = 0; i < 8; i++)
                value[4 + i] = (byte)(filetime >> (8 * i));
        }
        return value;
    }

    /// <summary>Extrait le chemin de l'exécutable d'une ligne de commande (guillemets gérés).</summary>
    internal static string ExtractExecutablePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : command.Trim('"');
        }

        var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? command[..(exeIndex + 4)] : command.Split(' ')[0];
    }

    internal static string EstimateImpact(string command)
    {
        var path = ExtractExecutablePath(command);
        var lower = path.ToLowerInvariant();

        if (KnownHeavyApps.Any(lower.Contains))
            return "élevé";

        try
        {
            if (File.Exists(path))
            {
                var sizeMb = new FileInfo(path).Length / 1024.0 / 1024.0;
                return sizeMb switch
                {
                    > 50 => "élevé (estimé)",
                    > 10 => "moyen (estimé)",
                    _ => "faible (estimé)"
                };
            }
        }
        catch { }

        return "inconnu";
    }
}
