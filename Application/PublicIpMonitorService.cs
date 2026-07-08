using System.Net;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Surveille l'adresse IP publique et signale tout changement — utile si un
/// service perso (serveur de jeu, NAS, accès distant) est hébergé derrière
/// cette connexion. Vérification à intervalle limité (15 min) pour rester
/// léger, jamais plus fréquent même si appelé à chaque cycle de monitoring.
/// </summary>
public sealed class PublicIpMonitorService
{
    private const string LookupUrl = "https://api.ipify.org";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly string _stateFile;
    private string? _lastKnownIp;
    private DateTime _lastCheck = DateTime.MinValue;

    public PublicIpMonitorService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "public-ip-state.json");
        LoadState();
    }

    public string? LastKnownIp => _lastKnownIp;

    /// <summary>Retourne la nouvelle IP si elle a changé depuis le dernier contrôle connu, sinon null.</summary>
    public async Task<string?> CheckForChangeAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow - _lastCheck < CheckInterval)
            return null;

        _lastCheck = DateTime.UtcNow;

        string currentIp;
        try
        {
            currentIp = (await Http.GetStringAsync(LookupUrl, ct).ConfigureAwait(false)).Trim();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Vérification de l'IP publique impossible (hors ligne ?)", ex);
            return null;
        }

        if (!IPAddress.TryParse(currentIp, out _))
            return null;

        var previous = _lastKnownIp;
        _lastKnownIp = currentIp;
        SaveState();

        return HasIpChanged(previous, currentIp) ? currentIp : null;
    }

    internal static bool HasIpChanged(string? previous, string current) =>
        previous is not null && !previous.Equals(current, StringComparison.Ordinal);

    private void LoadState()
    {
        try
        {
            if (File.Exists(_stateFile))
                _lastKnownIp = JsonSerializer.Deserialize<string>(File.ReadAllText(_stateFile));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'état de l'IP publique impossible", ex);
        }
    }

    private void SaveState()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_lastKnownIp));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde de l'état de l'IP publique impossible", ex);
        }
    }
}
