using System.Management;
using System.Text.RegularExpressions;

namespace ZiaMonitoring_App.Application;

public sealed record MemoryModuleRaw(string BankLabel, string DeviceLocator, int RatedSpeedMhz, int RunningSpeedMhz);

public enum ChannelStatus { Unknown, SingleChannel, SingleChannelMisconfigured, MultiChannel, LikelyMultiChannel, LikelyImbalanced }

public sealed record ChannelDiagnosis(ChannelStatus Status, int DetectedChannelCount, string Label);

public sealed record MemoryConfigDiagnosis(
    int ModuleCount,
    int RatedSpeedMhz,
    int RunningSpeedMhz,
    bool XmpLikelyDisabled,
    ChannelDiagnosis Channel)
{
    public string SpeedLabel => RatedSpeedMhz <= 0 || RunningSpeedMhz <= 0
        ? "Vitesse mémoire non lisible depuis le BIOS/WMI."
        : XmpLikelyDisabled
            ? $"RAM à {RunningSpeedMhz} MHz alors que le kit est annoncé pour {RatedSpeedMhz} MHz : le profil XMP/EXPO semble désactivé (vitesse JEDEC de base appliquée)."
            : $"RAM à {RunningSpeedMhz} MHz (annoncée {RatedSpeedMhz} MHz) : profil XMP/EXPO appliqué correctement.";
}

/// <summary>
/// Diagnostic mémoire en lecture seule : profil XMP/EXPO désactivé (vitesse
/// réelle très inférieure à la vitesse annoncée par les modules) et
/// configuration mono/multi-canal. Le canal exact n'est pas toujours exposé
/// par le BIOS via WMI : l'estimation le signale explicitement plutôt que
/// d'affirmer à tort.
/// </summary>
public static class MemoryConfigDiagnosticsService
{
    private const double XmpDisabledRatio = 0.9;

    public static MemoryConfigDiagnosis Analyze()
    {
        var modules = ReadModules();
        return Diagnose(modules);
    }

    private static IReadOnlyList<MemoryModuleRaw> ReadModules()
    {
        var result = new List<MemoryModuleRaw>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT BankLabel, DeviceLocator, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");

            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var bankLabel = item["BankLabel"]?.ToString() ?? string.Empty;
                var deviceLocator = item["DeviceLocator"]?.ToString() ?? string.Empty;
                var rated = item["Speed"] is uint s ? (int)s : 0;
                var running = item["ConfiguredClockSpeed"] is uint c ? (int)c : 0;
                result.Add(new MemoryModuleRaw(bankLabel, deviceLocator, rated, running));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture WMI de la configuration mémoire impossible", ex);
        }

        return result;
    }

    internal static MemoryConfigDiagnosis Diagnose(IReadOnlyList<MemoryModuleRaw> modules)
    {
        var ratedSpeed = modules.Count > 0 ? modules.Max(m => m.RatedSpeedMhz) : 0;
        var runningSpeed = modules.Count > 0 ? modules.Where(m => m.RunningSpeedMhz > 0).Select(m => m.RunningSpeedMhz).DefaultIfEmpty(0).Min() : 0;

        var xmpDisabled = IsXmpLikelyDisabled(ratedSpeed, runningSpeed);
        var locators = modules.Select(m => $"{m.BankLabel} {m.DeviceLocator}").ToList();
        var channel = DiagnoseChannel(locators);

        return new MemoryConfigDiagnosis(modules.Count, ratedSpeed, runningSpeed, xmpDisabled, channel);
    }

    internal static bool IsXmpLikelyDisabled(int ratedSpeedMhz, int runningSpeedMhz) =>
        ratedSpeedMhz > 0 && runningSpeedMhz > 0 && runningSpeedMhz < ratedSpeedMhz * XmpDisabledRatio;

    internal static ChannelDiagnosis DiagnoseChannel(IReadOnlyList<string> locators)
    {
        if (locators.Count == 0)
            return new ChannelDiagnosis(ChannelStatus.Unknown, 0, "Aucun module mémoire détecté.");

        if (locators.Count == 1)
            return new ChannelDiagnosis(ChannelStatus.SingleChannel, 1,
                "Une seule barrette installée : configuration mono-canal, jusqu'à moitié moins de bande passante mémoire qu'en dual-canal.");

        var channelLetters = locators
            .Select(ExtractChannelLetter)
            .Where(l => l is not null)
            .Select(l => l!.Value)
            .Distinct()
            .ToList();

        if (channelLetters.Count >= 2)
            return new ChannelDiagnosis(ChannelStatus.MultiChannel, channelLetters.Count,
                $"{locators.Count} barrette(s) réparties sur {channelLetters.Count} canaux distincts : configuration multi-canal confirmée.");

        if (channelLetters.Count == 1)
            return new ChannelDiagnosis(ChannelStatus.SingleChannelMisconfigured, 1,
                $"{locators.Count} barrettes détectées mais toutes semblent occuper le même canal : vérifiez leur emplacement dans le manuel de la carte mère (souvent 2 slots de la même couleur).");

        return locators.Count % 2 == 0
            ? new ChannelDiagnosis(ChannelStatus.LikelyMultiChannel, 0,
                $"{locators.Count} barrette(s) détectée(s), canal non identifiable depuis le BIOS : nombre pair, probablement multi-canal si les slots sont corrects.")
            : new ChannelDiagnosis(ChannelStatus.LikelyImbalanced, 0,
                $"{locators.Count} barrette(s) détectée(s), nombre impair : configuration probablement asymétrique entre les canaux (non optimal).");
    }

    internal static char? ExtractChannelLetter(string locator)
    {
        var match = Regex.Match(locator, @"channel\s*([A-Ha-h])", RegexOptions.IgnoreCase);
        return match.Success ? char.ToUpperInvariant(match.Groups[1].Value[0]) : null;
    }
}
