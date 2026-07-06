using System.Diagnostics;
using System.Net;
using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Application;

public sealed class ActiveGameDetector
{
    private static readonly HashSet<string> KnownGameProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "valorant", "valorant-win64-shipping", "league of legends", "leagueclient", "leagueoflegends",
        "csgo", "cs2", "dota2", "fortnite", "fortniteclient-win64-shipping",
        "minecraft", "javaw", "cod", "modernwarfare", "warzone",
        "overwatch", "overwatch2", "apexlegends", "rocketleague",
        "gta5", "gtav", "eldenring", "fifa", "r5apex", "destiny2",
        "rainbowsix", "r6", "battlefieldv", "bf1", "bf4",
        "pubg", "tslgame", "warframe", "borderlands3",
        "cyberpunk2077", "witcher3", "hogwartslegacy",
        "dota", "hearthstone", "starcraft", "diablo"
    };

    private readonly GameServerLatencyCollector _latencyCollector = new();
    private DateTime? _sessionStart;
    private string? _activeGame;

    public ActiveGameSession? DetectActiveGame(IReadOnlyList<TcpConnectionInfo> connections, double estimatedFps)
    {
        try
        {
            var candidates = Process.GetProcesses()
                .Where(process => KnownGameProcessNames.Contains(process.ProcessName))
                .OrderByDescending(SafeWorkingSet)
                .ToList();

            foreach (var process in candidates)
            {
                using (process)
                {
                    var name = process.ProcessName;
                    var pid = process.Id;
                    var memoryMb = SafeWorkingSet(process) / 1024.0 / 1024.0;

                    if (_activeGame != name)
                    {
                        _activeGame = name;
                        _sessionStart = DateTime.Now;
                    }

                    var duration = _sessionStart.HasValue
                        ? DateTime.Now - _sessionStart.Value
                        : TimeSpan.Zero;

                    var serverEndpoint = FindLikelyGameServer(connections, pid);
                    var serverPing = serverEndpoint is null
                        ? -1
                        : _latencyCollector.MeasureEndpointCached(serverEndpoint);

                    return new ActiveGameSession(
                        pid,
                        name,
                        0,
                        memoryMb,
                        duration,
                        estimatedFps,
                        serverEndpoint,
                        serverPing);
                }
            }
        }
        catch (Exception ex)
        {
            // Process enumeration can race with exiting games.
            Infrastructure.AppLog.Warn("Detection du jeu actif en echec", ex);
        }

        _activeGame = null;
        _sessionStart = null;
        return null;
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    internal static string? FindLikelyGameServer(IReadOnlyList<TcpConnectionInfo> connections, int pid)
    {
        foreach (var connection in connections)
        {
            if (connection.Pid != pid
                || !connection.State.Equals("Established", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var host = connection.RemoteEndpoint.Split(':')[0].Trim('[', ']');
            if (!IPAddress.TryParse(host, out var address) || !IsPublicAddress(address))
                continue;

            return connection.RemoteEndpoint;
        }

        return null;
    }

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        if (bytes[0] is 10 or 127)
            return false;

        if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            return false;

        if (bytes[0] == 192 && bytes[1] == 168)
            return false;

        if (bytes[0] == 169 && bytes[1] == 254)
            return false;

        return true;
    }
}
