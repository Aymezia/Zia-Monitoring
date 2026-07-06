using Microsoft.Win32;
using System.Management;
using System.Runtime.InteropServices;
using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class PcProfileCollector
{
    private static readonly string[] GameKeywords =
    [
        "valorant", "league of legends", "counter-strike", "cs2", "dota", "fortnite", "minecraft",
        "call of duty", "overwatch", "apex", "rocket league", "gta", "elden ring", "fifa", "ea sports",
        "battlefield", "pubg", "rainbow six", "r6", "destiny", "the finals", "warframe", "war thunder"
    ];

    private static readonly string[] GamePublisherKeywords =
    [
        "riot", "valve", "blizzard", "epic", "ubisoft", "electronic arts", "ea", "bethesda", "rockstar",
        "square enix", "bandai", "capcom", "2k", "activision", "cd projekt"
    ];

    public PcProfile Collect()
    {
        var machine = Environment.MachineName;
        var os = RuntimeInformation.OSDescription;
        var arch = RuntimeInformation.OSArchitecture.ToString();
        var cpu = ReadCpuModel();
        var gpu = ReadGpuModel();
        var motherboard = ReadMotherboard();
        var bios = ReadBiosVersion();
        var cores = Environment.ProcessorCount;
        var ramGb = ReadTotalRamGb();
        var disks = ReadDisks();
        var totalDiskGb = disks.Sum(d => d.TotalGb);
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var games = ReadInstalledGames();

        return new PcProfile(machine, os, arch, cpu, gpu, motherboard, bios, cores, ramGb, totalDiskGb, uptime, disks, games);
    }

    private static string ReadCpuModel()
    {
        var value = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return $"Unknown CPU ({RuntimeInformation.ProcessArchitecture})";
    }

    private static string ReadGpuModel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            // WMI can fail on restricted contexts.
            AppLog.Warn("Lecture WMI du profil materiel incomplete", ex);
        }

        return "Unknown GPU";
    }

    private static string ReadMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var manufacturer = item["Manufacturer"]?.ToString()?.Trim();
                var product = item["Product"]?.ToString()?.Trim();
                var value = string.Join(" ", new[] { manufacturer, product }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }
        catch (Exception ex)
        {
            // WMI can fail on restricted contexts.
            AppLog.Warn("Lecture WMI du profil materiel incomplete", ex);
        }

        return "Unknown Motherboard";
    }

    private static string ReadBiosVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion FROM Win32_BIOS");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var bios = item["SMBIOSBIOSVersion"]?.ToString();
                if (!string.IsNullOrWhiteSpace(bios))
                {
                    return bios.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            // WMI can fail on restricted contexts.
            AppLog.Warn("Lecture WMI du profil materiel incomplete", ex);
        }

        return "Unknown BIOS";
    }

    private static double ReadTotalRamGb()
    {
        if (!GetPhysicallyInstalledSystemMemory(out var totalKb))
        {
            return 0;
        }

        return totalKb / 1024d / 1024d;
    }

    private static IReadOnlyList<DiskProfile> ReadDisks()
    {
        var list = new List<DiskProfile>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            {
                continue;
            }

            var totalGb = drive.TotalSize / 1024d / 1024d / 1024d;
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            list.Add(new DiskProfile(drive.Name, drive.DriveFormat, totalGb, freeGb));
        }

        return list;
    }

    private static IReadOnlyList<GameInstallation> ReadInstalledGames()
    {
        var games = new Dictionary<string, GameInstallation>(StringComparer.OrdinalIgnoreCase);
        var playtimes = SteamPlaytimeReader.ReadPlaytimes();

        ReadGamesFromUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, games, playtimes);
        ReadGamesFromUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, games, playtimes);
        ReadGamesFromUninstall(RegistryHive.CurrentUser, RegistryView.Default, games, playtimes);
        ReadGamesFromSteamManifests(games, playtimes);

        return games.Values
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Take(300)
            .ToList();
    }

    private static void ReadGamesFromUninstall(RegistryHive hive, RegistryView view, Dictionary<string, GameInstallation> games, Dictionary<string, TimeSpan> playtimes)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false);
            if (uninstall is null)
                return;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(subName, false);
                if (item is null)
                    continue;

                var name = item.GetValue("DisplayName")?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var publisher = item.GetValue("Publisher")?.ToString() ?? string.Empty;
                var installLocation = item.GetValue("InstallLocation")?.ToString() ?? string.Empty;
                var version = item.GetValue("DisplayVersion")?.ToString() ?? "N/A";

                if (!LooksLikeGame(name, publisher, installLocation))
                    continue;

                var platform = DetectPlatform(name, installLocation);
                var launchUri = BuildLaunchUri(platform, name, subName, installLocation);
                var steamAppId = ExtractSteamAppId(registrySubName: subName);
                var coverUri = BuildCoverUri(steamAppId);
                playtimes.TryGetValue(name, out var playTime);

                var key = name.ToLowerInvariant();
                games[key] = new GameInstallation(
                    name,
                    platform,
                    version,
                    string.IsNullOrWhiteSpace(installLocation) ? "N/A" : installLocation,
                    playTime,
                    launchUri,
                    coverUri,
                    steamAppId);
            }
        }
        catch (Exception ex)
        {
            // Registry can be partially unavailable.
            AppLog.Warn("Inventaire des jeux: registre partiellement indisponible", ex);
        }
    }

    private static void ReadGamesFromSteamManifests(Dictionary<string, GameInstallation> games, Dictionary<string, TimeSpan> playtimes)
    {
        var steamRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        var steamApps = Path.Combine(steamRoot, "steamapps");
        if (!Directory.Exists(steamApps))
            return;

        foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).Take(500))
        {
            try
            {
                var lines = File.ReadLines(manifest).Take(120).ToList();

                var nameLine = lines.FirstOrDefault(l => l.Contains("\"name\"", StringComparison.OrdinalIgnoreCase));
                var appIdLine = lines.FirstOrDefault(l => l.Contains("\"appid\"", StringComparison.OrdinalIgnoreCase));
                if (nameLine is null)
                    continue;

                var parts = nameLine.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var gameName = parts.LastOrDefault();
                if (string.IsNullOrWhiteSpace(gameName))
                    continue;

                string? launchUri = null;
                if (appIdLine is not null)
                {
                    var idParts = appIdLine.Split('"', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var appId = idParts.LastOrDefault();
                    if (!string.IsNullOrWhiteSpace(appId))
                        launchUri = $"steam://rungameid/{appId}";
                }

                playtimes.TryGetValue(gameName, out var playTime);
                var key = gameName.ToLowerInvariant();
                if (!games.ContainsKey(key))
                {
                    var steamAppId = ExtractSteamAppIdFromLaunchUri(launchUri);
                    games[key] = new GameInstallation(
                        gameName,
                        "Steam",
                        "N/A",
                        steamApps,
                        playTime,
                        launchUri,
                        BuildCoverUri(steamAppId),
                        steamAppId);
                }
            }
            catch
            {
                // Ignore malformed manifests.
            }
        }
    }

    private static string? BuildLaunchUri(string platform, string name, string registrySubName, string installLocation)
    {
        if (platform == "Steam" && registrySubName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
        {
            var appId = registrySubName["Steam App ".Length..];
            if (appId.All(char.IsDigit))
                return $"steam://rungameid/{appId}";
        }
        if (platform == "Epic")
            return $"com.epicgames.launcher://apps/{Uri.EscapeDataString(name)}?action=launch";
        if (platform == "Riot" && name.Contains("valorant", StringComparison.OrdinalIgnoreCase))
            return "valorant://";
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            var exe = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (exe is not null)
                return exe;
        }
        return null;
    }

    private static string? ExtractSteamAppId(string registrySubName)
    {
        if (!registrySubName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase))
            return null;

        var appId = registrySubName["Steam App ".Length..];
        return appId.All(char.IsDigit) ? appId : null;
    }

    private static string? ExtractSteamAppIdFromLaunchUri(string? launchUri)
    {
        if (string.IsNullOrWhiteSpace(launchUri)
            || !launchUri.StartsWith("steam://rungameid/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var appId = launchUri["steam://rungameid/".Length..];
        return appId.All(char.IsDigit) ? appId : null;
    }

    private static string? BuildCoverUri(string? steamAppId)
    {
        return string.IsNullOrWhiteSpace(steamAppId)
            ? null
            : $"https://cdn.cloudflare.steamstatic.com/steam/apps/{steamAppId}/library_600x900.jpg";
    }

    private static bool LooksLikeGame(string name, string publisher, string installLocation)
    {
        if (GameKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (GamePublisherKeywords.Any(k => publisher.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (installLocation.Contains("steam", StringComparison.OrdinalIgnoreCase)
            || installLocation.Contains("epic", StringComparison.OrdinalIgnoreCase)
            || installLocation.Contains("riot", StringComparison.OrdinalIgnoreCase)
            || installLocation.Contains("battle.net", StringComparison.OrdinalIgnoreCase)
            || installLocation.Contains("gog", StringComparison.OrdinalIgnoreCase)
            || installLocation.Contains("ubisoft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string DetectPlatform(string name, string installLocation)
    {
        if (name.Contains("valorant", StringComparison.OrdinalIgnoreCase) || installLocation.Contains("riot", StringComparison.OrdinalIgnoreCase))
        {
            return "Riot";
        }

        if (installLocation.Contains("steam", StringComparison.OrdinalIgnoreCase))
        {
            return "Steam";
        }

        if (installLocation.Contains("epic", StringComparison.OrdinalIgnoreCase))
        {
            return "Epic";
        }

        if (installLocation.Contains("battle.net", StringComparison.OrdinalIgnoreCase) || installLocation.Contains("blizzard", StringComparison.OrdinalIgnoreCase))
        {
            return "Battle.net";
        }

        return "Windows";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out long totalMemoryInKilobytes);
}
