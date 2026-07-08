using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record WolDevice(string Name, string MacAddress);

/// <summary>
/// Réveil à distance d'un autre PC du réseau local par paquet magique
/// (Wake-on-LAN) — nécessite que la carte réseau cible ait le WOL activé
/// dans son BIOS/pilote. Les appareils enregistrés sont persistés en JSON.
/// </summary>
public sealed class WakeOnLanService
{
    private const int WolPort = 9;
    private readonly string _stateFile;
    private List<WolDevice> _devices = [];

    public WakeOnLanService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "wol-devices.json");
        LoadState();
    }

    public IReadOnlyList<WolDevice> GetDevices() => _devices;

    public (bool Success, string Message) AddDevice(string name, string mac)
    {
        var normalizedMac = NormalizeMac(mac);
        if (normalizedMac is null)
            return (false, "Adresse MAC invalide (format attendu : AA:BB:CC:DD:EE:FF).");

        if (string.IsNullOrWhiteSpace(name))
            return (false, "Un nom est requis.");

        _devices.RemoveAll(d => d.MacAddress.Equals(normalizedMac, StringComparison.OrdinalIgnoreCase));
        _devices.Add(new WolDevice(name.Trim(), normalizedMac));
        SaveState();
        return (true, $"'{name}' ajouté.");
    }

    public void RemoveDevice(string mac)
    {
        _devices.RemoveAll(d => d.MacAddress.Equals(mac, StringComparison.OrdinalIgnoreCase));
        SaveState();
    }

    public static (bool Success, string Message) Wake(string mac)
    {
        var normalizedMac = NormalizeMac(mac);
        if (normalizedMac is null)
            return (false, "Adresse MAC invalide.");

        try
        {
            var packet = BuildMagicPacket(normalizedMac);
            using var udp = new UdpClient { EnableBroadcast = true };
            udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, WolPort));
            return (true, "Paquet magique envoyé.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Envoi du paquet Wake-on-LAN vers '{mac}' impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>"aa-bb-cc-dd-ee-ff" / "AABBCCDDEEFF" → "AA:BB:CC:DD:EE:FF", null si invalide.</summary>
    internal static string? NormalizeMac(string mac)
    {
        var hex = new string((mac ?? string.Empty).Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12)
            return null;

        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2).ToUpperInvariant()));
    }

    /// <summary>6 octets 0xFF suivis de 16 répétitions de l'adresse MAC (102 octets au total).</summary>
    internal static byte[] BuildMagicPacket(string normalizedMac)
    {
        var macBytes = normalizedMac.Split(':').Select(b => Convert.ToByte(b, 16)).ToArray();
        var packet = new byte[6 + 16 * 6];
        for (var i = 0; i < 6; i++)
            packet[i] = 0xFF;
        for (var i = 0; i < 16; i++)
            Array.Copy(macBytes, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
                _devices = JsonSerializer.Deserialize<List<WolDevice>>(File.ReadAllText(_stateFile)) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des appareils Wake-on-LAN impossible", ex);
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_devices));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde des appareils Wake-on-LAN impossible", ex);
        }
    }
}
