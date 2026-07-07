using System.Net.NetworkInformation;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public enum ActiveConnectionKind { Wired, Wireless, Unknown }

public sealed class NetworkCollector
{
    /// <summary>
    /// Devine l'interface qui porte réellement le trafic (celle avec une
    /// passerelle par défaut et le plus de trafic cumulé), pas juste la
    /// première interface "up" — beaucoup de PC ont Wi-Fi et Ethernet actifs
    /// simultanément sans que les deux soient réellement utilisés.
    /// </summary>
    public static ActiveConnectionKind DetectActiveConnectionKind()
    {
        try
        {
            var best = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                         && n.GetIPProperties().GatewayAddresses.Count > 0)
                .OrderByDescending(n =>
                {
                    var stats = n.GetIPv4Statistics();
                    return stats.BytesSent + stats.BytesReceived;
                })
                .FirstOrDefault();

            if (best is null)
                return ActiveConnectionKind.Unknown;

            return best.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                ? ActiveConnectionKind.Wireless
                : ActiveConnectionKind.Wired;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Détection du type de connexion réseau impossible", ex);
            return ActiveConnectionKind.Unknown;
        }
    }

    private long _prevBytesSent;
    private long _prevBytesReceived;
    private DateTime _prevTime = DateTime.MinValue;

    public NetworkStats GetStats()
    {
        var pingMs = MeasurePing();
        var (upKbps, downKbps) = MeasureBandwidth();
        return new NetworkStats(upKbps, downKbps, pingMs);
    }

    private (double UpKbps, double DownKbps) MeasureBandwidth()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

            long sent = 0, received = 0;
            foreach (var iface in interfaces)
            {
                var stats = iface.GetIPv4Statistics();
                sent += stats.BytesSent;
                received += stats.BytesReceived;
            }

            var now = DateTime.UtcNow;

            if (_prevTime == DateTime.MinValue)
            {
                _prevBytesSent = sent;
                _prevBytesReceived = received;
                _prevTime = now;
                return (0, 0);
            }

            var elapsed = (now - _prevTime).TotalSeconds;
            if (elapsed <= 0)
                return (0, 0);

            var upKbps = (sent - _prevBytesSent) / elapsed / 1024.0;
            var downKbps = (received - _prevBytesReceived) / elapsed / 1024.0;

            _prevBytesSent = sent;
            _prevBytesReceived = received;
            _prevTime = now;

            return (Math.Max(0, upKbps), Math.Max(0, downKbps));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lecture des statistiques reseau impossible", ex);
            return (0, 0);
        }
    }

    private static double MeasurePing()
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send("8.8.8.8", 500);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
        }
        catch
        {
            return -1;
        }
    }
}
