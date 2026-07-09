using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Infrastructure;

namespace ZiaMonitoring_App.Pages;

/// <summary>
/// Point d'entrée UI unique pour demander l'élévation à la demande.
/// Affiche une boîte de dialogue expliquant pourquoi, relance l'app en
/// administrateur si l'utilisateur accepte (ce qui ferme le processus
/// courant), et retourne false dans tous les cas où l'action ne doit pas
/// se poursuivre dans CE processus.
/// </summary>
internal static class AdminElevationPrompt
{
    /// <param name="resumeArgs">
    /// Arguments transmis à l'instance élevée pour qu'elle termine
    /// automatiquement l'action interrompue par le redémarrage (voir
    /// App.PendingDebloatResume) — sans ça, l'utilisateur devrait recliquer
    /// l'action une fois l'app relancée, ce qui donne l'impression qu'elle
    /// "ne marche pas".
    /// </param>
    public static async Task<bool> EnsureElevatedAsync(XamlRoot xamlRoot, string actionDescription, IEnumerable<string>? resumeArgs = null)
    {
        if (AdminElevation.IsElevated)
            return true;

        var dialog = new ContentDialog
        {
            Title = "Droits administrateur requis",
            Content = $"{actionDescription} nécessite les droits administrateur. Relancer Zia Monitoring en administrateur ?",
            PrimaryButtonText = "Relancer en administrateur",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return false;

        if (AdminElevation.RelaunchElevated(resumeArgs))
        {
            Microsoft.UI.Xaml.Application.Current.Exit();
        }

        return false;
    }
}
