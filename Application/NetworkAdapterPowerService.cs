using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public sealed record NetworkAdapterPower(string InstanceKey, string Name, bool PowerOffAllowed)
{
    public string StatusLabel => PowerOffAllowed
        ? "Windows peut l'éteindre (risque de coupure)"
        : "Protégé (extinction interdite)";
}

/// <summary>
/// Détecte les cartes réseau pour lesquelles Windows a le droit de « couper
/// l'alimentation pour économiser l'énergie » — cause classique de
/// micro-coupures réseau et de déconnexions en jeu, surtout sur les portables.
/// Le réglage vit dans la clé PnPCapabilities de chaque adaptateur (bit 0x100
/// = extinction interdite). Modifier ce bit nécessite les droits
/// administrateur ; l'interprétation du bit est pure et testable.
/// </summary>
public sealed class NetworkAdapterPowerService
{
    private const string NetClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const int PowerOffDisabledBit = 0x100;

    private static readonly string[] VirtualAdapterMarkers =
    [
        "wan miniport", "microsoft", "virtual", "vpn", "tap-", "tunnel", "loopback",
        "vmware", "virtualbox", "hyper-v", "bluetooth", "wi-fi direct", "kernel debug",
        "npcap", "teredo", "isatap", "6to4", "wfp",
    ];

    public IReadOnlyList<NetworkAdapterPower> Scan()
    {
        var result = new List<NetworkAdapterPower>();
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(NetClassKey, writable: false);
            if (classKey is null)
                return result;

            foreach (var instance in classKey.GetSubKeyNames())
            {
                if (!Regex.IsMatch(instance, @"^\d{4}$"))
                    continue;

                using var adapter = classKey.OpenSubKey(instance, writable: false);
                var desc = adapter?.GetValue("DriverDesc")?.ToString();
                if (string.IsNullOrWhiteSpace(desc) || IsVirtual(desc))
                    continue;

                var pnpCaps = adapter!.GetValue("PnPCapabilities") is int caps ? caps : 0;
                result.Add(new NetworkAdapterPower(instance, desc, IsPowerOffAllowed(pnpCaps)));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de la gestion d'alimentation des cartes réseau impossible", ex);
        }

        return result;
    }

    public (bool Success, string Message) Protect(string instanceKey) => SetPowerOff(instanceKey, allow: false);
    public (bool Success, string Message) Restore(string instanceKey) => SetPowerOff(instanceKey, allow: true);

    private static (bool, string) SetPowerOff(string instanceKey, bool allow)
    {
        try
        {
            using var adapter = Registry.LocalMachine.OpenSubKey($@"{NetClassKey}\{instanceKey}", writable: true)
                ?? throw new InvalidOperationException("Adaptateur introuvable (droits administrateur requis).");

            var current = adapter.GetValue("PnPCapabilities") is int caps ? caps : 0;
            var updated = allow ? current & ~PowerOffDisabledBit : current | PowerOffDisabledBit;
            adapter.SetValue("PnPCapabilities", updated, RegistryValueKind.DWord);

            return (true, allow
                ? "Windows est de nouveau autorisé à éteindre cette carte (réglage par défaut). Réactivez/redémarrez l'adaptateur pour appliquer."
                : "Extinction de la carte réseau désactivée. Réactivez/redémarrez l'adaptateur (ou redémarrez) pour appliquer.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Modification de l'alimentation de la carte '{instanceKey}' impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Bit 0x100 posé = extinction interdite ; absent = Windows peut éteindre la carte.</summary>
    internal static bool IsPowerOffAllowed(int pnpCapabilities) => (pnpCapabilities & PowerOffDisabledBit) == 0;

    internal static bool IsVirtual(string driverDesc) =>
        VirtualAdapterMarkers.Any(m => driverDesc.Contains(m, StringComparison.OrdinalIgnoreCase));
}
