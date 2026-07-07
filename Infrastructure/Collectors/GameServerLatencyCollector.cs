using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

/// <summary>
/// Perte de paquets mesurée sur une rafale de pings ICMP. Si l'ICMP est
/// entièrement bloqué par le réseau/pare-feu, HasData est faux — on ne
/// veut pas afficher "100% de perte" alors que c'est juste l'ICMP qui est
/// filtré (cas très courant, pas un problème réseau réel).
/// </summary>
public sealed record PacketLossReading(string Provider, string Host, double LossPercent, double AvgRttMs, bool HasData)
{
    public string LossLabel => HasData ? $"{LossPercent:F0}% perte" : "N/A (ICMP bloqué)";
    public string RttLabel => HasData && AvgRttMs > 0 ? $"{AvgRttMs:F0} ms" : "-";
}

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

    /// <summary>
    /// Mesure la perte de paquets sur les 4 cibles connues via une rafale de
    /// pings ICMP (appel bloquant ~0,5-1 s par cible, à lancer à la demande
    /// depuis l'UI, pas en continu).
    /// </summary>
    public IReadOnlyList<PacketLossReading> MeasureAllPacketLoss()
    {
        var probes = Targets
            .Select(t => Task.Run(() => MeasurePacketLoss(t.Provider, t.Host)))
            .ToArray();

        Task.WaitAll(probes);
        return probes.Select(p => p.Result).ToList();
    }

    private static PacketLossReading MeasurePacketLoss(string provider, string host, int count = 10, int timeoutMs = 400, int intervalMs = 40)
    {
        var samples = new List<double?>(count);
        for (var i = 0; i < count; i++)
        {
            try
            {
                using var ping = new Ping();
                var reply = ping.Send(host, timeoutMs);
                samples.Add(reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
            }
            catch
            {
                samples.Add(null);
            }

            if (i < count - 1)
                Thread.Sleep(intervalMs);
        }

        var (lossPercent, avgRtt) = ComputeLossStats(samples);
        var hasData = samples.Any(s => s.HasValue);
        return new PacketLossReading(provider, host, lossPercent, avgRtt, hasData);
    }

    /// <summary>Pure et testable : null = paquet perdu, sinon RTT en ms.</summary>
    internal static (double LossPercent, double AvgRttMs) ComputeLossStats(IReadOnlyList<double?> samples)
    {
        if (samples.Count == 0)
            return (0, -1);

        var received = samples.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var lossPercent = (samples.Count - received.Count) * 100.0 / samples.Count;
        var avgRtt = received.Count > 0 ? received.Average() : -1;
        return (lossPercent, avgRtt);
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
