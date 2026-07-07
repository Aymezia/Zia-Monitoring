using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record DeviceAccessEntry(string Device, string AppName, DateTime? LastUsedStart, DateTime? LastUsedStop)
{
    private static readonly DateTime NeverValue = DateTime.FromFileTime(0);

    public bool IsCurrentlyActive => LastUsedStart is not null
        && (LastUsedStop is null || LastUsedStop == NeverValue);

    public string LastUsedLabel => IsCurrentlyActive
        ? "En cours d'utilisation"
        : LastUsedStop is { } stop && stop > NeverValue
            ? $"Dernier accès : {stop:dd/MM HH:mm}"
            : "Jamais";
}

/// <summary>
/// Audit des accès webcam/microphone : lit l'historique que Windows tient
/// lui-même par app dans CapabilityAccessManager\ConsentStore (la même
/// source que Paramètres > Confidentialité > Caméra/Micro). Lecture seule,
/// aucune modification de permission.
/// </summary>
public sealed class DeviceAccessAuditService
{
    public IReadOnlyList<DeviceAccessEntry> ScanWebcamAccess() => ScanDevice("webcam", "Webcam");

    public IReadOnlyList<DeviceAccessEntry> ScanMicrophoneAccess() => ScanDevice("microphone", "Microphone");

    private static IReadOnlyList<DeviceAccessEntry> ScanDevice(string registryDeviceKey, string deviceLabel)
    {
        var results = new List<DeviceAccessEntry>();

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{registryDeviceKey}",
                writable: false);
            if (root is null)
                return results;

            foreach (var subKeyName in root.GetSubKeyNames())
            {
                if (subKeyName.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    using var nonPackaged = root.OpenSubKey(subKeyName);
                    if (nonPackaged is null)
                        continue;

                    foreach (var appKeyName in nonPackaged.GetSubKeyNames())
                    {
                        using var appKey = nonPackaged.OpenSubKey(appKeyName);
                        results.Add(BuildEntry(deviceLabel, CleanAppName(appKeyName), appKey));
                    }
                }
                else
                {
                    using var appKey = root.OpenSubKey(subKeyName);
                    results.Add(BuildEntry(deviceLabel, CleanAppName(subKeyName), appKey));
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Audit d'accès {deviceLabel} impossible", ex);
        }

        return results
            .Where(e => e.LastUsedStart is not null || e.LastUsedStop is not null)
            .OrderByDescending(e => e.LastUsedStop ?? e.LastUsedStart ?? DateTime.MinValue)
            .ToList();
    }

    private static DeviceAccessEntry BuildEntry(string device, string appName, RegistryKey? key)
    {
        var start = ConvertFileTime(key?.GetValue("LastUsedTimeStart"));
        var stop = ConvertFileTime(key?.GetValue("LastUsedTimeStop"));
        return new DeviceAccessEntry(device, appName, start, stop);
    }

    internal static DateTime? ConvertFileTime(object? rawValue)
    {
        if (rawValue is not long raw || raw <= 0)
            return null;

        try
        {
            return DateTime.FromFileTime(raw);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Les apps non empaquetées sont stockées avec leur chemin complet,
    /// "\" remplacé par "#" (ex: "C#Users#x#Discord#Discord.exe") : on ne
    /// garde que le nom du fichier. Les apps empaquetées (UWP) gardent leur
    /// PackageFamilyName tel quel — pas de résolution de nom convivial ici.
    /// </summary>
    internal static string CleanAppName(string rawKeyName)
    {
        if (!rawKeyName.Contains('#'))
            return rawKeyName;

        var cleaned = rawKeyName.Replace('#', '\\');
        var lastSlash = cleaned.LastIndexOf('\\');
        return lastSlash >= 0 && lastSlash < cleaned.Length - 1 ? cleaned[(lastSlash + 1)..] : cleaned;
    }
}
