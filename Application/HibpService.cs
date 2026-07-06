using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Vérification de mots de passe compromis via l'API « range » de
/// HaveIBeenPwned (k-anonymat : seuls les 5 premiers caractères du hash
/// SHA-1 sont envoyés, jamais le mot de passe ni son hash complet).
/// Gratuit et sans clé API.
/// </summary>
public sealed class HibpService
{
    private const string RangeApi = "https://api.pwnedpasswords.com/range/";

    /// <summary>
    /// Retourne le nombre d'occurrences du mot de passe dans les fuites
    /// connues (0 = introuvable), ou null si le service est injoignable.
    /// </summary>
    public async Task<long?> CheckPasswordAsync(string password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
            return 0;

        var (prefix, suffix) = HashAndSplit(password);

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZiaMonitoring", "1.0"));
            // Le padding ajoute de fausses entrées côté serveur : personne ne
            // peut déduire du volume de réponse quel hash était recherché.
            http.DefaultRequestHeaders.Add("Add-Padding", "true");

            var body = await http.GetStringAsync(RangeApi + prefix, ct).ConfigureAwait(false);
            return CountMatches(body, suffix);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Vérification HaveIBeenPwned impossible", ex);
            return null;
        }
    }

    internal static (string Prefix, string Suffix) HashAndSplit(string password)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        return (hash[..5], hash[5..]);
    }

    internal static long CountMatches(string rangeResponse, string suffix)
    {
        foreach (var line in rangeResponse.Split('\n'))
        {
            var parts = line.Trim().Split(':');
            if (parts.Length == 2
                && parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && long.TryParse(parts[1], out var count))
            {
                return count;
            }
        }
        return 0;
    }
}
