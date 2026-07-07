using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Application;

public sealed record RegionalLatency(string Region, string Host, double PingMs, bool IsReachable, bool IsBest = false)
{
    public string PingLabel => IsReachable ? $"{PingMs:F0} ms" : "N/A";
}

/// <summary>
/// Mesure la latence vers des régions de datacenters majeurs (points de
/// présence AWS, publics et stables) pour recommander la région la plus
/// proche — la plupart des jeux multijoueurs hébergent leurs serveurs dans
/// ces mêmes régions cloud. Recommandation uniquement : contrairement à un
/// tunnel VPN type ExitLag/WTFast, aucune route réseau n'est modifiée (cela
/// nécessiterait un pilote réseau noyau, hors périmètre d'une app de
/// monitoring).
/// </summary>
public sealed class RegionalLatencyService
{
    private static readonly (string Region, string Host)[] Regions =
    [
        ("US Est (Virginie)", "ec2.us-east-1.amazonaws.com"),
        ("US Ouest (Californie)", "ec2.us-west-1.amazonaws.com"),
        ("Europe (Irlande)", "ec2.eu-west-1.amazonaws.com"),
        ("Europe (Francfort)", "ec2.eu-central-1.amazonaws.com"),
        ("Asie (Tokyo)", "ec2.ap-northeast-1.amazonaws.com"),
        ("Asie (Singapour)", "ec2.ap-southeast-1.amazonaws.com"),
        ("Amérique du Sud (São Paulo)", "ec2.sa-east-1.amazonaws.com"),
        ("Australie (Sydney)", "ec2.ap-southeast-2.amazonaws.com")
    ];

    /// <summary>Mesure toutes les régions en parallèle. Retourne le classement, meilleure en premier.</summary>
    public IReadOnlyList<RegionalLatency> MeasureAll()
    {
        var probes = Regions
            .Select(r => Task.Run(() =>
            {
                var (pingMs, ok) = GameServerLatencyCollector.MeasureHost(r.Host, 700);
                return new RegionalLatency(r.Region, r.Host, pingMs, ok);
            }))
            .ToArray();

        Task.WaitAll(probes);
        return RankResults(probes.Select(p => p.Result).ToList());
    }

    internal static IReadOnlyList<RegionalLatency> RankResults(IReadOnlyList<RegionalLatency> raw)
    {
        var sorted = raw.OrderBy(r => r.IsReachable ? r.PingMs : double.MaxValue).ToList();
        if (sorted.Count > 0 && sorted[0].IsReachable)
            sorted[0] = sorted[0] with { IsBest = true };
        return sorted;
    }
}
