using System.Management;
using System.Security.Cryptography;

namespace ZiaMonitoring_App.Application;

public sealed record DiskForecast(string DriveRoot, double FreeGb, double AverageGameSizeGb)
{
    public int EstimatedGamesRemaining => (int)Math.Floor(FreeGb / AverageGameSizeGb);
    public string Label => EstimatedGamesRemaining > 0
        ? $"Encore environ {EstimatedGamesRemaining} jeu(x) AAA installable(s) sur {DriveRoot} ({FreeGb:F0} Go libres, base {AverageGameSizeGb:F0} Go/jeu)."
        : $"Moins de {AverageGameSizeGb:F0} Go libres sur {DriveRoot} : plus assez de place pour un jeu AAA moyen.";
}

public sealed record ShaderCacheEntry(string Label, string Path, double SizeMb)
{
    public string SizeLabel => SizeMb >= 1024 ? $"{SizeMb / 1024:F1} Go" : $"{SizeMb:F0} Mo";
}

public sealed record PageFileInfo(bool AutomaticallyManaged, double CurrentAllocatedMb, double RecommendedMinMb, double RecommendedMaxMb)
{
    public string Recommendation => AutomaticallyManaged
        ? "Géré automatiquement par Windows (recommandé pour la plupart des utilisateurs)."
        : $"Géré manuellement : {CurrentAllocatedMb:F0} Mo alloués actuellement. Windows recommande generalement {RecommendedMinMb:F0}-{RecommendedMaxMb:F0} Mo pour cette quantité de RAM.";
}

public sealed record DuplicateGroup(long FileSize, IReadOnlyList<string> Paths)
{
    public double WastedMb => FileSize * (Paths.Count - 1) / 1024.0 / 1024.0;
    public string Label => $"{Paths.Count} copies ({FileSize / 1024.0 / 1024.0:F1} Mo chacune, {WastedMb:F1} Mo gaspillés)";
}

public sealed record LargeItem(string Path, double SizeMb, bool IsDirectory);

/// <summary>
/// Outils d'analyse disque à la demande : prévision d'espace, cache shaders
/// (Steam/NVIDIA/AMD/Intel), fichier d'échange, doublons, gros fichiers.
/// Tout est lecture seule sauf CleanShaderCache (suppression explicite,
/// confirmée par l'utilisateur avant l'appel).
/// </summary>
public sealed class DiskMaintenanceService
{
    private const double DefaultAverageGameSizeGb = 90;

    public static DiskForecast ForecastRemainingGameSlots(string driveRoot, double averageGameSizeGb = DefaultAverageGameSizeGb)
    {
        try
        {
            var drive = new DriveInfo(driveRoot);
            var freeGb = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            return new DiskForecast(driveRoot, freeGb, averageGameSizeGb);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Lecture de l'espace libre sur '{driveRoot}' impossible", ex);
            return new DiskForecast(driveRoot, 0, averageGameSizeGb);
        }
    }

    public static IReadOnlyList<ShaderCacheEntry> ScanShaderCaches()
    {
        var results = new List<ShaderCacheEntry>();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        AddIfExists(results, "NVIDIA (DirectX)", Path.Combine(localAppData, "NVIDIA", "DXCache"));
        AddIfExists(results, "NVIDIA (OpenGL)", Path.Combine(localAppData, "NVIDIA", "GLCache"));
        AddIfExists(results, "AMD (DirectX)", Path.Combine(localAppData, "AMD", "DxCache"));
        AddIfExists(results, "AMD (DXC)", Path.Combine(localAppData, "AMD", "DxcCache"));
        AddIfExists(results, "Intel", Path.Combine(localAppData, "Intel", "ShaderCache"));

        var steamShaderRoot = Path.Combine(programFilesX86, "Steam", "steamapps", "shadercache");
        if (Directory.Exists(steamShaderRoot))
        {
            foreach (var appDir in SafeEnumerateDirectories(steamShaderRoot).Take(300))
            {
                var sizeMb = DirectorySizeMb(appDir);
                if (sizeMb > 1)
                    results.Add(new ShaderCacheEntry($"Steam (AppID {Path.GetFileName(appDir)})", appDir, sizeMb));
            }
        }

        return results.OrderByDescending(r => r.SizeMb).ToList();
    }

    private static void AddIfExists(List<ShaderCacheEntry> results, string label, string path)
    {
        if (!Directory.Exists(path))
            return;

        var sizeMb = DirectorySizeMb(path);
        if (sizeMb > 0.1)
            results.Add(new ShaderCacheEntry(label, path, sizeMb));
    }

    /// <summary>Supprime le contenu d'un dossier de cache shader (le dossier lui-même est conservé, régénéré au besoin par le pilote/jeu).</summary>
    public static (bool Success, string Message) CleanShaderCache(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return (true, "Déjà absent.");

            var freedMb = DirectorySizeMb(path);
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                try
                {
                    if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                    else File.Delete(entry);
                }
                catch
                {
                    // Fichier verrouillé (pilote actif) : ignoré, pas bloquant.
                }
            }

            return (true, $"Cache vidé : ~{freedMb:F0} Mo libérés.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Nettoyage du cache shader '{path}' impossible", ex);
            return (false, ex.Message);
        }
    }

    public static PageFileInfo AnalyzePageFile()
    {
        try
        {
            bool automaticallyManaged;
            using (var csSearcher = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
            {
                var cs = csSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                automaticallyManaged = cs?["AutomaticManagedPagefile"] is true;
            }

            double currentMb = 0;
            using (var pfSearcher = new ManagementObjectSearcher("SELECT AllocatedBaseSize FROM Win32_PageFileUsage"))
            {
                foreach (var pf in pfSearcher.Get().Cast<ManagementObject>())
                {
                    if (pf["AllocatedBaseSize"] is uint size)
                        currentMb += size;
                }
            }

            double totalRamMb = 0;
            using (var memSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                var cs = memSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
                if (cs?["TotalPhysicalMemory"] is ulong bytes)
                    totalRamMb = bytes / 1024.0 / 1024.0;
            }

            return new PageFileInfo(automaticallyManaged, currentMb, totalRamMb, totalRamMb * 1.5);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Analyse du fichier d'échange impossible", ex);
            return new PageFileInfo(true, 0, 0, 0);
        }
    }

    /// <summary>Regroupe par taille puis hash SHA256 (évite de hasher des fichiers de taille unique). Plafonné à 20000 fichiers.</summary>
    public static IReadOnlyList<DuplicateGroup> FindDuplicates(string folder, CancellationToken ct = default)
    {
        var bySize = new Dictionary<long, List<string>>();

        foreach (var file in SafeEnumerateFiles(folder).Take(20000))
        {
            ct.ThrowIfCancellationRequested();
            long size;
            try { size = new FileInfo(file).Length; }
            catch { continue; }

            if (size == 0)
                continue;

            if (!bySize.TryGetValue(size, out var list))
                bySize[size] = list = [];
            list.Add(file);
        }

        var groups = new List<DuplicateGroup>();
        foreach (var (size, paths) in bySize.Where(kvp => kvp.Value.Count > 1))
        {
            ct.ThrowIfCancellationRequested();
            var byHash = new Dictionary<string, List<string>>();
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var hash = TryHashFile(path);
                if (hash is null)
                    continue;

                if (!byHash.TryGetValue(hash, out var list))
                    byHash[hash] = list = [];
                list.Add(path);
            }

            foreach (var (_, samePaths) in byHash.Where(kvp => kvp.Value.Count > 1))
                groups.Add(new DuplicateGroup(size, samePaths));
        }

        return groups.OrderByDescending(g => g.WastedMb).ToList();
    }

    /// <summary>Fichiers individuels les plus gros + sous-dossiers immédiats les plus gros (taille cumulée).</summary>
    public static (IReadOnlyList<LargeItem> Files, IReadOnlyList<LargeItem> Folders) FindLargestItems(string folder, int top = 25)
    {
        var files = SafeEnumerateFiles(folder)
            .Take(50000)
            .Select(f =>
            {
                try { return new LargeItem(f, new FileInfo(f).Length / 1024.0 / 1024.0, false); }
                catch { return null; }
            })
            .Where(x => x is not null)
            .Cast<LargeItem>()
            .OrderByDescending(x => x.SizeMb)
            .Take(top)
            .ToList();

        var folders = SafeEnumerateDirectories(folder)
            .Select(d => new LargeItem(d, DirectorySizeMb(d), true))
            .OrderByDescending(x => x.SizeMb)
            .Take(top)
            .ToList();

        return (files, folders);
    }

    private static string? TryHashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch
        {
            return null;
        }
    }

    private static double DirectorySizeMb(string path)
    {
        long total = 0;
        foreach (var file in SafeEnumerateFiles(path).Take(50000))
        {
            try { total += new FileInfo(file).Length; }
            catch { }
        }
        return total / 1024.0 / 1024.0;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string path)
    {
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).GetEnumerator();
        }
        catch
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                bool has;
                try { has = enumerator.MoveNext(); }
                catch { yield break; }

                if (!has) yield break;
                yield return enumerator.Current;
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path);
        }
        catch
        {
            return [];
        }
    }
}
