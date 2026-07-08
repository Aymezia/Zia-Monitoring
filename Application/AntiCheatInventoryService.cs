using System.Management;

namespace ZiaMonitoring_App.Application;

public sealed record AntiCheatDriverInfo(string Name, string DriverFile, string Publisher, bool Installed, bool Running)
{
    public string StatusLabel => !Installed ? "Non détecté" : Running ? "Actif" : "Installé, inactif";
}

/// <summary>
/// Inventaire des drivers anti-cheat kernel connus (détection best-effort par
/// nom de fichier .sys + état du service WMI) — angle transparence : savoir
/// quel logiciel tiers a un accès noyau sur la machine, pas un outil de
/// contournement. Lecture seule.
/// </summary>
public static class AntiCheatInventoryService
{
    private static readonly (string DriverFile, string Name, string Publisher)[] KnownAntiCheats =
    [
        ("EasyAntiCheat.sys", "Easy Anti-Cheat", "Epic Games — Fortnite, Apex Legends, Rust..."),
        ("BEDaisy.sys", "BattlEye", "Multi-éditeurs — PUBG, Rainbow Six Siege, Fortnite..."),
        ("vgk.sys", "Riot Vanguard", "Riot Games — Valorant"),
        ("FACEIT-AC.sys", "FACEIT Anti-Cheat", "FACEIT — CS2 en compétitif"),
        ("mhyprot2.sys", "mhyprot2", "miHoYo/HoYoverse — Genshin Impact, Honkai...")
    ];

    public static IReadOnlyList<AntiCheatDriverInfo> Scan()
    {
        var results = new List<AntiCheatDriverInfo>();
        foreach (var (file, name, publisher) in KnownAntiCheats)
        {
            var (installed, running) = QueryDriver(file);
            results.Add(new AntiCheatDriverInfo(name, file, publisher, installed, running));
        }
        return results;
    }

    private static (bool Installed, bool Running) QueryDriver(string driverFileName)
    {
        try
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var path = Path.Combine(systemDir, "drivers", driverFileName);
            if (!File.Exists(path))
                return (false, false);

            var serviceName = Path.GetFileNameWithoutExtension(driverFileName);
            using var searcher = new ManagementObjectSearcher(
                $"SELECT State FROM Win32_SystemDriver WHERE Name='{serviceName}'");
            var running = searcher.Get().Cast<ManagementObject>()
                .Any(o => string.Equals(o["State"]?.ToString(), "Running", StringComparison.OrdinalIgnoreCase));

            return (true, running);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Inventaire anti-cheat: verification de '{driverFileName}' impossible", ex);
            return (false, false);
        }
    }
}
