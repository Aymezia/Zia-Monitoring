using System.Management;
using System.Text.RegularExpressions;

namespace ZiaMonitoring_App.Application;

public sealed record SteamGame(string Name, double SizeGb);

public sealed record SteamLibrary(string Path, string DriveLetter, string MediaType, IReadOnlyList<SteamGame> Games)
{
    public double TotalSizeGb => Games.Sum(g => g.SizeGb);
    public bool IsOnHdd => MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase);

    public string Label => $"{DriveLetter} — {MediaType} · {Games.Count} jeu(x) · {TotalSizeGb:F0} Go";

    public string Advice => IsOnHdd
        ? "Sur disque dur mécanique : déplacer les jeux les plus joués vers un SSD réduirait fortement les temps de chargement (Steam > clic droit sur le jeu > Propriétés > Fichiers locaux > Déplacer)."
        : "Sur SSD : temps de chargement optimaux.";
}

/// <summary>
/// Carte des bibliothèques Steam : quel jeu est sur quel disque, pour quelle
/// taille, et si ce disque est un SSD ou un HDD — croisement utile car un
/// gros jeu sur disque mécanique double ses temps de chargement sans qu'on
/// s'en rende compte. Le parsing du fichier libraryfolders.vdf et des
/// manifestes .acf est pur et testable ; le type de média provient de WMI.
/// </summary>
public sealed class SteamLibraryService
{
    public IReadOnlyList<SteamLibrary> Scan()
    {
        var steamRoot = FindSteamRoot();
        if (steamRoot is null)
            return [];

        var vdfPath = System.IO.Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
            return [];

        IReadOnlyList<string> libraryPaths;
        try
        {
            libraryPaths = ParseLibraryPaths(File.ReadAllText(vdfPath));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de libraryfolders.vdf impossible", ex);
            return [];
        }

        var driveMediaTypes = BuildDriveMediaTypeMap();
        var result = new List<SteamLibrary>();

        foreach (var libPath in libraryPaths)
        {
            var games = ScanLibraryGames(libPath);
            var driveLetter = System.IO.Path.GetPathRoot(libPath)?.TrimEnd('\\') ?? "?";
            var mediaType = driveMediaTypes.TryGetValue(driveLetter.TrimEnd(':'), out var mt) ? mt : "Inconnu";
            result.Add(new SteamLibrary(libPath, driveLetter, mediaType, games));
        }

        return result;
    }

    internal static IReadOnlyList<string> ParseLibraryPaths(string vdfContent)
    {
        var paths = new List<string>();
        foreach (Match match in Regex.Matches(vdfContent, @"""path""\s*""([^""]+)"""))
        {
            // Les chemins VDF échappent les antislashs : "C:\\Games" → C:\Games
            var path = match.Groups[1].Value.Replace(@"\\", @"\");
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }
        return paths;
    }

    private static IReadOnlyList<SteamGame> ScanLibraryGames(string libraryPath)
    {
        var steamApps = System.IO.Path.Combine(libraryPath, "steamapps");
        if (!Directory.Exists(steamApps))
            return [];

        var games = new List<SteamGame>();
        foreach (var manifest in SafeEnumerate(steamApps, "appmanifest_*.acf"))
        {
            try
            {
                var game = ParseAppManifest(File.ReadAllText(manifest));
                if (game is not null)
                    games.Add(game);
            }
            catch { }
        }

        return games.OrderByDescending(g => g.SizeGb).ToList();
    }

    internal static SteamGame? ParseAppManifest(string acfContent)
    {
        var name = Regex.Match(acfContent, @"""name""\s*""([^""]*)""").Groups[1].Value;
        var sizeMatch = Regex.Match(acfContent, @"""SizeOnDisk""\s*""(\d+)""");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var sizeGb = sizeMatch.Success && long.TryParse(sizeMatch.Groups[1].Value, out var bytes)
            ? bytes / 1024.0 / 1024.0 / 1024.0
            : 0;

        return new SteamGame(name, sizeGb);
    }

    private static string? FindSteamRoot()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
            if (key?.GetValue("SteamPath") is string path && Directory.Exists(path))
                return path;
        }
        catch { }

        var fallback = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>Map lettre de lecteur (« C ») → type de média, via la jointure MSFT_Partition → MSFT_PhysicalDisk.</summary>
    private static Dictionary<string, string> BuildDriveMediaTypeMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var diskMediaByNumber = new Dictionary<uint, string>();
            using (var disks = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"))
            {
                foreach (var disk in disks.Get().Cast<ManagementObject>())
                {
                    if (uint.TryParse(disk["DeviceId"]?.ToString(), out var deviceId) && disk["MediaType"] is ushort mt)
                        diskMediaByNumber[deviceId] = DiskOptimizationAuditService.MediaTypeLabel(mt);
                }
            }

            using var partitions = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT DriveLetter, DiskNumber FROM MSFT_Partition");
            foreach (var partition in partitions.Get().Cast<ManagementObject>())
            {
                var letter = partition["DriveLetter"]?.ToString();
                if (string.IsNullOrWhiteSpace(letter) || letter == "\0")
                    continue;

                if (partition["DiskNumber"] is uint diskNumber && diskMediaByNumber.TryGetValue(diskNumber, out var media))
                    map[letter] = media;
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Association lecteur → type de disque impossible", ex);
        }

        return map;
    }

    private static IEnumerable<string> SafeEnumerate(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }
}
