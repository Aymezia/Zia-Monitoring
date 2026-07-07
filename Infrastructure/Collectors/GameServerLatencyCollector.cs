using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed class GameServerLatencyCollector
{
    private static readonly (string Provider, string Host)[] Targets =
    [
        ("Riot", "riotgames.com"),
        ("Valve", "steamcommunity.com"),
        ("EA", "ea.com"),
        ("Epic", "epicgames.com")
    ];

    private readonly Dictionary<string, (DateTime CheckedAt, double PingMs, bool IsReachable)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastBulkCheck = DateTime.MinValue;
    private IReadOnlyList<GameServerLatency> _lastBulkResult = Array.Empty<GameServerLatency>();

    public IReadOnlyList<GameServerLatency> GetGameServerLatencies()
    {
        if ((DateTime.UtcNow - _lastBulkCheck).TotalSeconds < 8)
            return _lastBulkResult;

        _lastBulkCheck = DateTime.UtcNow;

        // Les 4 cibles sont mesurées en parallèle : le pire cas passe de
        // ~4 × (450 ms + fallback TCP) en série à un seul timeout.
        var probes = Targets
            .Select(target => Task.Run(() =>
            {
                var (pingMs, ok) = MeasureHost(target.Host, 450);
                return new GameServerLatency(target.Provider, target.Host, pingMs, ok);
            }))
            .ToArray();

        Task.WaitAll(probes);
        _lastBulkResult = probes.Select(p => p.Result).ToList();

        return _lastBulkResult;
    }

    public double MeasureEndpointCached(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return -1;

        var endpoint = host.Split(':')[0].Trim('[', ']');
        if (_cache.TryGetValue(endpoint, out var cached)
            && (DateTime.UtcNow - cached.CheckedAt).TotalSeconds < 10)
        {
            return cached.IsReachable ? cached.PingMs : -1;
        }

        var (pingMs, ok) = MeasureHost(endpoint, 250);
        _cache[endpoint] = (DateTime.UtcNow, pingMs, ok);
        return ok ? pingMs : -1;
    }

    /// <summary>Ping ICMP avec repli TCP:443 si l'ICMP est bloqué. Réutilisé par RegionalLatencyService.</summary>
    internal static (double PingMs, bool IsReachable) MeasureHost(string host, int timeoutMs)
    {
        try
        {
            using var ping = new Ping();
            var reply = ping.Send(host, timeoutMs);
            if (reply.Status == IPStatus.Success)
                return (reply.RoundtripTime, true);
        }
        catch
        {
            // ICMP is often blocked; use TCP below.
        }

        try
        {
            using var client = new TcpClient();
            var sw = Stopwatch.StartNew();
            var task = client.ConnectAsync(host, 443);
            if (!task.Wait(timeoutMs))
                return (-1, false);

            sw.Stop();
            return (sw.Elapsed.TotalMilliseconds, true);
        }
        catch
        {
            return (-1, false);
        }
    }
}
