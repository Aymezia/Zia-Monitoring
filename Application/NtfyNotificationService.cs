using System.Text;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Notification déportée sur téléphone via ntfy.sh (aucun compte requis :
/// le "topic" choisi par l'utilisateur fait office de canal privé — à garder
/// non devinable). Titre et message sont fusionnés dans le corps plutôt que
/// dans l'en-tête HTTP "Title" pour éviter les soucis d'encodage ASCII avec
/// les accents français.
/// </summary>
public static class NtfyNotificationService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static async Task SendAsync(string topic, string title, string message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return;

        try
        {
            using var content = new StringContent($"{title}\n{message}", Encoding.UTF8);
            using var response = await Http.PostAsync($"https://ntfy.sh/{Uri.EscapeDataString(topic)}", content, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Envoi de notification ntfy.sh impossible", ex);
        }
    }
}
