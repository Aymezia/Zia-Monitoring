using System.Net.NetworkInformation;
using System.Text;

namespace ZiaMonitoring_App.Infrastructure.Collectors;

public sealed record TracerouteHop(int HopNumber, string? Address, double? RoundtripMs, bool IsDestination)
{
    public string AddressLabel => Address ?? "* (aucune réponse)";
    public string RttLabel => RoundtripMs is { } ms ? $"{ms:F0} ms" : "-";
}

/// <summary>
/// Traceroute par TTL croissant (même technique que tracert.exe / traceroute
/// Unix) : chaque routeur intermédiaire qui laisse expirer le paquet répond
/// avec son adresse, révélant le chemin réseau saut par saut — utile pour
/// voir où la latence augmente (FAI local vs backbone vs proche de la
/// destination), sans dépendre du parsing de la sortie de tracert.exe.
/// </summary>
public static class TracerouteService
{
    private const int MaxHops = 30;
    private const int TimeoutMs = 1000;

    public static IReadOnlyList<TracerouteHop> Trace(string destinationHost)
    {
        var hops = new List<TracerouteHop>();
        var buffer = Encoding.ASCII.GetBytes("ziamonitoring-traceroute");

        try
        {
            using var ping = new Ping();

            for (var ttl = 1; ttl <= MaxHops; ttl++)
            {
                var options = new PingOptions(ttl, dontFragment: true);
                PingReply reply;
                try
                {
                    reply = ping.Send(destinationHost, TimeoutMs, buffer, options);
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Traceroute: échec au saut {ttl}", ex);
                    hops.Add(new TracerouteHop(ttl, null, null, false));
                    continue;
                }

                var hasResponse = reply.Status is IPStatus.Success or IPStatus.TtlExpired;
                hops.Add(new TracerouteHop(
                    ttl,
                    hasResponse ? reply.Address?.ToString() : null,
                    hasResponse ? reply.RoundtripTime : null,
                    reply.Status == IPStatus.Success));

                if (reply.Status == IPStatus.Success)
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Traceroute vers '{destinationHost}' impossible", ex);
        }

        return hops;
    }
}
