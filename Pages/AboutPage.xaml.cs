using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;

namespace ZiaMonitoring_App.Pages;

public sealed partial class AboutPage : Page
{
    private string? _releaseUrl;
    private UpdateInfo? _latestUpdate;
    private bool _loading = true;

    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Version {UpdateCheckService.CurrentVersion()}";

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        AutoUpdateToggle.IsOn = app.SettingsService.Load().EnableAutoUpdateInstall;
        _loading = false;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusLabel.Text = "Verification en cours…";
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        UpdateNowButton.Visibility = Visibility.Collapsed;
        _releaseUrl = null;
        _latestUpdate = null;

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
                _latestUpdate = update;
                UpdateStatusLabel.Text = $"Nouvelle version disponible : {update.TagName}";
                OpenReleaseButton.Visibility = Visibility.Visible;
                UpdateNowButton.Visibility = update.PortableZipUrl is not null || update.SetupExeUrl is not null
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_latestUpdate is null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Mettre à jour Zia Monitoring",
            Content = $"La version {_latestUpdate.TagName} va être téléchargée et installée. L'application va se fermer puis redémarrer automatiquement. Continuer ?",
            PrimaryButtonText = "Mettre à jour",
            CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ApplyUpdateAsync(_latestUpdate);
    }

    private async Task ApplyUpdateAsync(UpdateInfo update)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateNowButton.IsEnabled = false;

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var (success, message) = await app.SelfUpdater.UpdateAsync(update, text =>
        {
            DispatcherQueue.TryEnqueue(() => UpdateStatusLabel.Text = text);
        });

        UpdateStatusLabel.Text = message;
        if (success)
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        else
        {
            CheckUpdateButton.IsEnabled = true;
            UpdateNowButton.IsEnabled = true;
        }
    }

    private void AutoUpdate_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var settings = app.SettingsService.Load();
        app.SettingsService.Save(settings with { EnableAutoUpdateInstall = AutoUpdateToggle.IsOn });
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
