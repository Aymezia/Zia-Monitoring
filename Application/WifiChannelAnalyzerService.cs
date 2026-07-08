using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ZiaMonitoring_App.Application;

public sealed record WifiNetworkObservation(string Ssid, int Channel, int SignalPercent);

public sealed record WifiChannelAnalysis(
    int? CurrentChannel,
    bool Is5GhzOrAbove,
    IReadOnlyDictionary<int, int> NetworksPerChannel,
    int? RecommendedChannel)
{
    public string Label
    {
        get
        {
            if (CurrentChannel is null)
                return "Pas de connexion Wi-Fi active (ou interface non détectée par netsh).";

            if (Is5GhzOrAbove)
                return $"Connecté sur le canal {CurrentChannel} (bande 5 GHz ou supérieure) : peu de risque de saturation de canal, aucune recommandation nécessaire.";

            var crowding = NetworksPerChannel.TryGetValue(CurrentChannel.Value, out var count) ? count : 0;
            if (RecommendedChannel is null || RecommendedChannel == CurrentChannel)
                return $"Canal {CurrentChannel} (2,4 GHz) : {crowding} réseau(x) voisin(s) sur ce canal, déjà optimal ou proche parmi 1/6/11.";

            return $"Canal {CurrentChannel} (2,4 GHz) : {crowding} réseau(x) voisin(s) détecté(s) sur ce canal. "
                 + $"Le canal {RecommendedChannel} est moins encombré — envisagez de le régler dans l'interface de votre routeur.";
        }
    }
}

/// <summary>
/// Analyse du canal Wi-Fi 2,4 GHz : détecte les réseaux voisins par canal via
/// "netsh wlan show networks" et recommande le canal le moins encombré parmi
/// les 3 non-superposés (1, 6, 11). Aucune action n'est appliquée
/// automatiquement — le changement de canal se fait côté routeur.
/// </summary>
public static class WifiChannelAnalyzerService
{
    private static readonly int[] NonOverlappingChannels = [1, 6, 11];

    public static WifiChannelAnalysis Analyze()
    {
        var currentChannel = ParseCurrentChannel(RunNetsh("wlan show interfaces"));
        var networks = ParseNetworks(RunNetsh("wlan show networks mode=bssid"));
        return BuildAnalysis(currentChannel, networks);
    }

    private static string RunNetsh(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh", arguments)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null)
                return string.Empty;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Commande 'netsh {arguments}' impossible", ex);
            return string.Empty;
        }
    }

    internal static int? ParseCurrentChannel(string output)
    {
        var match = Regex.Match(output, @"^\s*(Canal|Channel)\s*:\s*(\d+)", RegexOptions.Multiline);
        return match.Success && int.TryParse(match.Groups[2].Value, out var channel) ? channel : null;
    }

    internal static IReadOnlyList<WifiNetworkObservation> ParseNetworks(string output)
    {
        var result = new List<WifiNetworkObservation>();
        string? currentSsid = null;
        var pendingSignal = 0;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            var ssidMatch = Regex.Match(line, @"^\s*SSID\s+\d+\s*:\s*(.*)$");
            if (ssidMatch.Success)
            {
                currentSsid = ssidMatch.Groups[1].Value.Trim();
                pendingSignal = 0;
                continue;
            }

            var signalMatch = Regex.Match(line, @"^\s*Signal\s*:\s*(\d+)\s*%");
            if (signalMatch.Success)
            {
                pendingSignal = int.Parse(signalMatch.Groups[1].Value);
                continue;
            }

            var channelMatch = Regex.Match(line, @"^\s*(Canal|Channel)\s*:\s*(\d+)");
            if (channelMatch.Success && currentSsid is not null && int.TryParse(channelMatch.Groups[2].Value, out var channel))
            {
                result.Add(new WifiNetworkObservation(currentSsid, channel, pendingSignal));
            }
        }

        return result;
    }

    internal static WifiChannelAnalysis BuildAnalysis(int? currentChannel, IReadOnlyList<WifiNetworkObservation> networks)
    {
        var perChannel = networks
            .GroupBy(n => n.Channel)
            .ToDictionary(g => g.Key, g => g.Count());

        if (currentChannel is null)
            return new WifiChannelAnalysis(null, false, perChannel, null);

        var is5GhzOrAbove = currentChannel > 14;
        if (is5GhzOrAbove)
            return new WifiChannelAnalysis(currentChannel, true, perChannel, null);

        var recommended = NonOverlappingChannels
            .OrderBy(c => perChannel.TryGetValue(c, out var count) ? count : 0)
            .ThenBy(c => c == currentChannel ? 0 : 1)
            .First();

        return new WifiChannelAnalysis(currentChannel, false, perChannel, recommended);
    }
}
