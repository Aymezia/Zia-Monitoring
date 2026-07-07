using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record IpGeoInfo(string Ip, string Country, string CountryCode, string Org)
{
    public string Label => string.IsNullOrWhiteSpace(Org)
        ? $"{Country} ({CountryCode})"
        : $"{Country} ({CountryCode}) · {Org}";
}

/// <summary>
/// Géolocalisation des adresses IP sortantes via l'API batch d'ip-api.com
/// (gratuite, sans clé, 100 IP par requête). Lookup uniquement à la demande
/// de l'utilisateur — jamais automatique — et cache mémoire pour respecter
/// la limite de débit. Le tier gratuit est en HTTP : seules des adresses IP
/// publiques transitent, aucune donnée personnelle.
/// </summary>
public sealed class GeoIpService
{
    private const string BatchApi = "http://ip-api.com/batch?fields=status,country,countryCode,org,isp,query";
    private const int MaxIpsPerBatch = 100;

    private readonly Dictionary<string, IpGeoInfo> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>
    /// Résout les IP publiques de la liste (port ignoré). Retourne les
    /// informations connues, cache inclus ; les IP privées sont ignorées.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IpGeoInfo>> LookupAsync(
        IEnumerable<string> endpoints, CancellationToken ct = default)
    {
        var publicIps = endpoints
            .Select(ExtractPublicIp)
            .Where(ip => ip is not null)
            .Select(ip => ip!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> missing;
        lock (_gate)
        {
            missing = publicIps.Where(ip => !_cache.ContainsKey(ip)).Take(MaxIpsPerBatch).ToList();
        }

        if (missing.Count > 0)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZiaMonitoring", "1.0"));

                var payload = JsonSerializer.Serialize(missing);
                using var response = await http.PostAsync(
                    BatchApi,
                    new StringContent(payload, Encoding.UTF8, "application/json"),
                    ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var results = ParseBatchResponse(json);

                lock (_gate)
                {
                    foreach (var info in results)
                        _cache[info.Ip] = info;
                }
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("Géolocalisation ip-api.com impossible", ex);
            }
        }

        lock (_gate)
        {
            return publicIps
                .Where(_cache.ContainsKey)
                .ToDictionary(ip => ip, ip => _cache[ip], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>« 51.91.10.20:443 » → « 51.91.10.20 » ; null si privée/invalide.</summary>
    internal static string? ExtractPublicIp(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return null;

        var host = endpoint.Split(':')[0].Trim('[', ']');
        if (!IPAddress.TryParse(host, out var address))
            return null;

        return ActiveGameDetector.IsPublicAddress(address) ? host : null;
    }

    internal static IReadOnlyList<IpGeoInfo> ParseBatchResponse(string json)
    {
        var result = new List<IpGeoInfo>();
        using var doc = JsonDocument.Parse(json);

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("status", out var status) || status.GetString() != "success")
                continue;

            var ip = item.TryGetProperty("query", out var q) ? q.GetString() : null;
            if (string.IsNullOrEmpty(ip))
                continue;

            var org = item.TryGetProperty("org", out var o) ? o.GetString() : null;
            if (string.IsNullOrWhiteSpace(org) && item.TryGetProperty("isp", out var isp))
                org = isp.GetString();

            result.Add(new IpGeoInfo(
                ip,
                item.TryGetProperty("country", out var c) ? c.GetString() ?? "?" : "?",
                item.TryGetProperty("countryCode", out var cc) ? cc.GetString() ?? "??" : "??",
                org ?? string.Empty));
        }

        return result;
    }
}
