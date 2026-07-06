using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;

namespace ZiaMonitoring_App.Pages;

public sealed partial class AboutPage : Page
{
    private string? _releaseUrl;

    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {UpdateCheckService.CurrentVersion()}";
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusLabel.Text = "Verification en cours…";
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        _releaseUrl = null;

        try
        {
            var app = (App)Microsoft.UI.Xaml.Application.Current;
            var update = await app.UpdateChecker.CheckForUpdateAsync();

            if (update is null)
            {
                UpdateStatusLabel.Text = $"Vous etes a jour (version {UpdateCheckService.CurrentVersion()}).";
            }
            else
            {
                _releaseUrl = update.ReleaseUrl;
                UpdateStatusLabel.Text = $"Nouvelle version disponible : {update.TagName}";
                OpenReleaseButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            UpdateStatusLabel.Text = "Verification impossible (hors ligne ?).";
            Infrastructure.AppLog.Warn("Verification de mise a jour depuis la page A propos en echec", ex);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_releaseUrl))
            return;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(_releaseUrl) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Ouverture de la page de release impossible", ex);
        }
    }
}
