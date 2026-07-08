using System.Management;

namespace ZiaMonitoring_App.Application;

public sealed record AntivirusProductInfo(string Name, bool IsEnabled)
{
    public string EnabledLabel => IsEnabled ? "Actif" : "Inactif";
}

/// <summary>
/// Inventaire des antivirus enregistrés auprès du Centre de sécurité Windows
/// (root\SecurityCenter2). Plusieurs antivirus tiers actifs en même temps
/// est un conflit fréquent et invisible : chacun intercepte les mêmes
/// fichiers, provoquant des ralentissements sans message d'erreur clair.
/// </summary>
public static class AntivirusInventoryService
{
    public static IReadOnlyList<AntivirusProductInfo> ScanInstalled()
    {
        var result = new List<AntivirusProductInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");

            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var name = obj["displayName"]?.ToString() ?? "Antivirus inconnu";
                var state = obj["productState"] is int s ? s : 0;
                result.Add(new AntivirusProductInfo(name, IsAntivirusEnabled(state)));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du Centre de sécurité Windows impossible", ex);
        }

        return result;
    }

    /// <summary>
    /// Décodage du bitmask productState (technique documentée par la
    /// communauté pour SecurityCenter2) : l'octet du milieu indique si la
    /// protection temps réel est active (0x10/0x11).
    /// </summary>
    internal static bool IsAntivirusEnabled(int productState)
    {
        var hex = ((uint)productState).ToString("X6").PadLeft(6, '0');
        var middleByte = Convert.ToInt32(hex.Substring(2, 2), 16);
        return middleByte is 0x10 or 0x11;
    }

    internal static (bool HasConflict, IReadOnlyList<string> EnabledNames) DetectConflict(IReadOnlyList<AntivirusProductInfo> products)
    {
        var enabled = products.Where(p => p.IsEnabled).Select(p => p.Name).ToList();
        return (enabled.Count > 1, enabled);
    }
}
