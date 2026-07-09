using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record InstalledSoftwareEntry(string Name, string Publisher, string? InstallLocation, string Version);

public sealed record DuplicateSoftwareGroup(string NormalizedName, IReadOnlyList<string> InstallLocations)
{
    public string Label => $"{NormalizedName} installé à {InstallLocations.Count} emplacements distincts : {string.Join(" · ", InstallLocations)}";
}

/// <summary>
/// Audit des logiciels installés (registre Uninstall) : installations
/// dupliquées à des emplacements distincts (souvent le résultat d'une
/// réinstallation sans désinstaller l'ancienne) et logiciels d'essai encore
/// présents. Contrairement au débloat (apps préinstallées Windows), ceci
/// vise les logiciels tiers installés par l'utilisateur. Les familles de
/// redistribuables (VC++, .NET, DirectX…) sont exclues du contrôle de
/// doublons : plusieurs versions/architectures y sont normales et voulues.
/// </summary>
public static class InstalledSoftwareAuditService
{
    private static readonly string[] ExcludedDuplicateFamilies =
    [
        "microsoft visual c++", "microsoft .net", ".net framework", ".net desktop runtime", ".net runtime",
        "windows software development kit", "directx", "java", "microsoft edge webview2",
        "microsoft update health tools", "microsoft edge update"
    ];

    private static readonly string[] TrialKeywords =
    [
        "trial", "essai", "démo", "demo", "shareware", "évaluation", "evaluation"
    ];

    public static IReadOnlyList<InstalledSoftwareEntry> ReadInstalledSoftware()
    {
        var entries = new Dictionary<string, InstalledSoftwareEntry>(StringComparer.OrdinalIgnoreCase);
        ReadFromHive(RegistryHive.LocalMachine, RegistryView.Registry64, entries);
        ReadFromHive(RegistryHive.LocalMachine, RegistryView.Registry32, entries);
        ReadFromHive(RegistryHive.CurrentUser, RegistryView.Default, entries);
        return entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IReadOnlyList<DuplicateSoftwareGroup> ScanDuplicates() => DetectDuplicates(ReadInstalledSoftware());

    public static IReadOnlyList<InstalledSoftwareEntry> ScanTrialSoftware() =>
        ReadInstalledSoftware().Where(e => IsTrialCandidate(e.Name)).ToList();

    private static void ReadFromHive(RegistryHive hive, RegistryView view, Dictionary<string, InstalledSoftwareEntry> entries)
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

                if (item.GetValue("SystemComponent") is int sc && sc == 1)
                    continue;

                var name = item.GetValue("DisplayName")?.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name, @"^KB\d+$", RegexOptions.IgnoreCase))
                    continue;

                var publisher = item.GetValue("Publisher")?.ToString() ?? string.Empty;
                var installLocation = item.GetValue("InstallLocation")?.ToString();
                var version = item.GetValue("DisplayVersion")?.ToString() ?? "N/A";

                entries[name] = new InstalledSoftwareEntry(name, publisher,
                    string.IsNullOrWhiteSpace(installLocation) ? null : installLocation!.TrimEnd('\\'), version);
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des logiciels installés (registre) partiellement indisponible", ex);
        }
    }

    internal static IReadOnlyList<DuplicateSoftwareGroup> DetectDuplicates(IReadOnlyList<InstalledSoftwareEntry> entries)
    {
        return entries
            .Where(e => e.InstallLocation is not null && !IsExcludedFamily(e.Name))
            .GroupBy(e => NormalizeName(e.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(e => e.InstallLocation!.ToLowerInvariant()).Distinct().Count() > 1)
            .Select(g => new DuplicateSoftwareGroup(
                g.First().Name,
                g.Select(e => e.InstallLocation!).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .ToList();
    }

    internal static bool IsExcludedFamily(string name) =>
        ExcludedDuplicateFamilies.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

    internal static bool IsTrialCandidate(string name) =>
        TrialKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Retire les suffixes de version/architecture pour regrouper les variantes d'un même produit.</summary>
    internal static string NormalizeName(string name)
    {
        var normalized = name.Trim();
        normalized = Regex.Replace(normalized, @"\s*\(?(x86|x64|32-bit|64-bit)\)?\s*$", "", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+v?\d+(\.\d+){1,3}[a-z]?\s*$", "", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s+\d{4}\s*$", "");
        return normalized.Trim().ToLowerInvariant();
    }
}
