using System.Management;

namespace ZiaMonitoring_App.Application;

public sealed record SsdWearInfo(string Name, int? WearPercent, double? PowerOnHours)
{
    public string Label => WearPercent switch
    {
        { } wear when wear >= 90 => $"{Name} : usure estimée {wear}% — remplacement à envisager bientôt.",
        { } wear => $"{Name} : usure estimée {wear}% ({100 - wear}% de vie restante estimée).",
        null => $"{Name} : usure non exposée par ce modèle de disque (compteur SMART non supporté par ce contrôleur)."
    };
}

/// <summary>
/// Usure des SSD via les compteurs de fiabilité de stockage Windows
/// (root\Microsoft\Windows\Storage, MSFT_StorageReliabilityCounter — la même
/// source que "Get-PhysicalDisk | Get-StorageReliabilityCounter" en
/// PowerShell). Le "Wear" en pourcentage n'est exposé que par certains
/// contrôleurs SSD ; les autres restent affichés sans ce chiffre plutôt que
/// d'inventer une valeur.
/// </summary>
public static class SsdWearService
{
    public static IReadOnlyList<SsdWearInfo> Analyze()
    {
        var result = new List<SsdWearInfo>();
        try
        {
            using var diskSearcher = new ManagementObjectSearcher(
                @"root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, MediaType FROM MSFT_PhysicalDisk");

            foreach (var disk in diskSearcher.Get().Cast<ManagementObject>())
            {
                var deviceId = disk["DeviceId"]?.ToString();
                var name = disk["FriendlyName"]?.ToString() ?? "Disque inconnu";
                var mediaType = disk["MediaType"] is ushort mt ? mt : (ushort)0;

                // MediaType 4 = SSD, 5 = SCM ; les HDD (3) n'ont pas de notion d'usure par écriture.
                if (mediaType != 4 && mediaType != 5)
                    continue;

                if (string.IsNullOrEmpty(deviceId))
                    continue;

                int? wear = null;
                double? powerOnHours = null;
                try
                {
                    using var relSearcher = new ManagementObjectSearcher(
                        @"root\Microsoft\Windows\Storage",
                        $"ASSOCIATORS OF {{MSFT_PhysicalDisk.DeviceId='{deviceId}'}} WHERE AssocClass=MSFT_PhysicalDiskToStorageReliabilityCounter");

                    foreach (var rel in relSearcher.Get().Cast<ManagementObject>())
                    {
                        if (rel["Wear"] is byte w)
                            wear = w;
                        else if (rel["Wear"] is ushort w2)
                            wear = w2;

                        if (rel["PowerOnHours"] is ulong hours)
                            powerOnHours = hours;
                    }
                }
                catch (Exception ex)
                {
                    Infrastructure.AppLog.Warn($"Compteurs de fiabilité indisponibles pour '{name}'", ex);
                }

                result.Add(new SsdWearInfo(name, wear, powerOnHours));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture WMI des disques physiques impossible", ex);
        }

        return result;
    }
}
