using System.Net.NetworkInformation;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class NetworkCollector
{
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
        catch
        {
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
