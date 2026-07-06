using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record UpdateInfo(string TagName, string ReleaseUrl, Version Version);

/// <summary>
/// Vérifie sur les GitHub Releases si une version plus récente est publiée.
/// N'installe rien : signale la mise à jour et ouvre la page de release.
/// </summary>
public sealed class UpdateCheckService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Aymezia/Zia-Monitoring/releases/latest";

    /// <summary>
    /// Retourne les informations de mise à jour si une version plus récente
    /// que l'assembly courant existe, sinon null. Ne lève jamais (retourne
    /// null en cas d'échec réseau ou de tag non parsable).
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            // L'API GitHub exige un User-Agent.
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZiaMonitoring", CurrentVersion().ToString()));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var json = await http.GetStringAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(url))
                return null;

            var latest = ParseVersion(tag);
            if (latest is null)
            {
                Infrastructure.AppLog.Info($"Verification de mise a jour: tag non parsable '{tag}'");
                return null;
            }

            return latest > CurrentVersion() ? new UpdateInfo(tag, url, latest) : null;
        }
        catch (Exception ex)
        {
            // Hors-ligne, rate-limit GitHub, repo sans release : silencieux.
            Infrastructure.AppLog.Info($"Verification de mise a jour impossible: {ex.Message}");
            return null;
        }
    }

    public static Version CurrentVersion() =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

    /// <summary>Parse "v1.2.3", "1.2.3" ou "v1.2" vers une Version.</summary>
    internal static Version? ParseVersion(string tag)
    {
        var cleaned = tag.Trim().TrimStart('v', 'V');
        var dashIndex = cleaned.IndexOf('-');
        if (dashIndex > 0)
            cleaned = cleaned[..dashIndex]; // ignore les suffixes -beta, -rc1…

        if (!cleaned.Contains('.'))
            cleaned += ".0";

        return Version.TryParse(cleaned, out var version) ? version : null;
    }
}
