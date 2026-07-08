using System.Diagnostics;
using System.Net.NetworkInformation;

namespace ZiaMonitoring_App.Application;

public enum DnsProvider { Automatic, Cloudflare, Google, Quad9 }

public sealed record DnsProviderOption(DnsProvider Provider, string Label, string? PrimaryIp, string? SecondaryIp);

public sealed record DnsLatencyResult(string Label, string Ip, double? AvgRttMs, bool Reachable)
{
    public bool IsBest { get; init; }
    public string RttLabel => Reachable && AvgRttMs is { } ms ? $"{ms:F0} ms" : "Injoignable";
}

public sealed record VpnDetectionResult(bool IsActive, string? AdapterName)
{
    public string Label => IsActive
        ? $"VPN détecté : {AdapterName}. Le ping mesuré peut inclure le détour par le VPN."
        : "Aucun VPN détecté.";
}

/// <summary>
/// Bascule le DNS de l'interface active (Cloudflare/Google/Quad9/automatique),
/// compare leur latence, active le DNS-over-HTTPS et signale la présence
/// d'un VPN. Modifie uniquement la configuration IP locale via netsh —
/// aucune route n'est altérée, tout est réversible en repassant en
/// "Automatique".
/// </summary>
public sealed class DnsService
{
    public static readonly IReadOnlyList<DnsProviderOption> Providers =
    [
        new(DnsProvider.Automatic, "Automatique (box/FAI)", null, null),
        new(DnsProvider.Cloudflare, "Cloudflare", "1.1.1.1", "1.0.0.1"),
        new(DnsProvider.Google, "Google", "8.8.8.8", "8.8.4.4"),
        new(DnsProvider.Quad9, "Quad9", "9.9.9.9", "149.112.112.112")
    ];

    private static readonly string[] VpnAdapterPatterns =
    [
        "wireguard", "wintun", "openvpn", "tap-windows", "tap0901", "nordlynx", "nordvpn",
        "expressvpn", "mullvad", "protonvpn", "surfshark", "tunnelbear", "hotspot shield",
        "cisco anyconnect", "forticlient", "globalprotect", "zerotier", "tailscale",
        "hamachi", "private internet access", "ipvanish"
    ];

    /// <summary>Nom de l'interface qui porte réellement le trafic (passerelle + plus de trafic), pour cibler netsh.</summary>
    public static string? GetActiveAdapterName()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                         && n.GetIPProperties().GatewayAddresses.Count > 0)
                .OrderByDescending(n =>
                {
                    var stats = n.GetIPv4Statistics();
                    return stats.BytesSent + stats.BytesReceived;
                })
                .FirstOrDefault()?.Name;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Détection de l'interface active impossible", ex);
            return null;
        }
    }

    public static VpnDetectionResult DetectVpn()
    {
        try
        {
            var match = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                    && IsVpnAdapter(n.Name, n.Description));

            return new VpnDetectionResult(match is not null, match?.Name ?? match?.Description);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Détection VPN impossible", ex);
            return new VpnDetectionResult(false, null);
        }
    }

    internal static bool IsVpnAdapter(string name, string description) =>
        VpnAdapterPatterns.Any(p =>
            name.Contains(p, StringComparison.OrdinalIgnoreCase)
            || description.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Applique le fournisseur choisi à l'interface active via netsh. "Automatique" repasse en DHCP.</summary>
    public (bool Success, string Message) SetProvider(DnsProvider provider)
    {
        var adapter = GetActiveAdapterName();
        if (adapter is null)
            return (false, "Aucune interface réseau active détectée.");

        var option = Providers.First(p => p.Provider == provider);

        try
        {
            if (provider == DnsProvider.Automatic)
            {
                var (code, _, err) = RunProcess("netsh", $"interface ip set dns name=\"{adapter}\" source=dhcp");
                return code == 0
                    ? (true, $"DNS de '{adapter}' repassé en automatique.")
                    : (false, $"Échec : {FirstNonEmpty(err)}");
            }

            var (setCode, _, setErr) = RunProcess("netsh",
                $"interface ip set dns name=\"{adapter}\" source=static addr={option.PrimaryIp} register=none");
            if (setCode != 0)
                return (false, $"Échec : {FirstNonEmpty(setErr)}");

            if (!string.IsNullOrEmpty(option.SecondaryIp))
                RunProcess("netsh", $"interface ip add dns name=\"{adapter}\" addr={option.SecondaryIp} index=2");

            return (true, $"DNS de '{adapter}' basculé sur {option.Label} ({option.PrimaryIp}).");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Bascule DNS vers '{provider}' impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Active/désactive le DNS-over-HTTPS pour le résolveur courant (Windows 11 22H2+ requis).</summary>
    public (bool Success, string Message) SetDnsOverHttps(bool enable)
    {
        try
        {
            var adapter = GetActiveAdapterName();
            if (adapter is null)
                return (false, "Aucune interface réseau active détectée.");

            var value = enable ? "Enable" : "Disable";
            var (code, _, err) = RunProcess("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"Get-DnsClientServerAddress -InterfaceAlias '{adapter}' -AddressFamily IPv4 | ForEach-Object {{ $_.ServerAddresses }} | ForEach-Object {{ Set-DnsClientDohServerAddress -ServerAddress $_ -DohTemplate ((Get-DnsClientDohServerAddress -ServerAddress $_ -ErrorAction SilentlyContinue).DohTemplate) -AllowFallbackToUdp ${(!enable).ToString().ToLowerInvariant()} -AutoUpgrade ${enable.ToString().ToLowerInvariant()} }}\"");

            return code == 0
                ? (true, $"DNS-over-HTTPS {(enable ? "activé" : "désactivé")}.")
                : (false, $"Indisponible sur cette version de Windows : {FirstNonEmpty(err)}");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Bascule DNS-over-HTTPS impossible", ex);
            return (false, ex.Message);
        }
    }

    /// <summary>Ping chaque fournisseur public + le DNS actuellement configuré, marque le plus rapide.</summary>
    public static IReadOnlyList<DnsLatencyResult> MeasureLatencies()
    {
        var results = new List<DnsLatencyResult>();

        foreach (var option in Providers.Where(p => p.PrimaryIp is not null))
        {
            var avg = PingAverage(option.PrimaryIp!);
            results.Add(new DnsLatencyResult(option.Label, option.PrimaryIp!, avg, avg is not null));
        }

        return MarkFastest(results);
    }

    /// <summary>Marque le résultat joignable le plus rapide comme IsBest. Séparé de la mesure réseau pour être testable.</summary>
    internal static IReadOnlyList<DnsLatencyResult> MarkFastest(IReadOnlyList<DnsLatencyResult> results)
    {
        var best = results.Where(r => r.Reachable).OrderBy(r => r.AvgRttMs).FirstOrDefault();
        if (best is null)
            return results;

        return results.Select(r => r with { IsBest = ReferenceEquals(r, best) }).ToList();
    }

    private static double? PingAverage(string ip, int attempts = 3)
    {
        var rtts = new List<long>();
        try
        {
            using var ping = new Ping();
            for (var i = 0; i < attempts; i++)
            {
                var reply = ping.Send(ip, 800);
                if (reply?.Status == IPStatus.Success)
                    rtts.Add(reply.RoundtripTime);
            }
        }
        catch
        {
            // Injoignable : on retourne ce qui a pu être mesuré.
        }

        return rtts.Count > 0 ? rtts.Average() : null;
    }

    private static string FirstNonEmpty(string text) => text.Trim() is { Length: > 0 } t ? t : "erreur inconnue.";

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, int timeoutMs = 8000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de démarrer {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return (process.ExitCode, output, error);
    }
}
