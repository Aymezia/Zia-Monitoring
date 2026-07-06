using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class ProfilesPage : Page
{
    private readonly App _app;

    public ProfilesPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        ProfilesList.ItemsSource = _app.OptimizationProfileService.GetProfiles();
    }

    private async void ApplyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string profileName)
            return;

        btn.IsEnabled = false;
        try
        {
            var (success, actions, warnings) = await Task.Run(
                () => _app.OptimizationProfileService.Apply(profileName));

            var msg = "Actions appliquees:\n" + string.Join("\n", actions.Select(a => $"  - {a}"));
            if (warnings.Count > 0)
                msg += "\n\nAvertissements:\n" + string.Join("\n", warnings.Select(w => $"  ! {w}"));

            await ShowDialog(success ? $"Profil '{profileName}' applique" : "Echec", msg);
        }
        finally
        {
            btn.IsEnabled = true;
        }
    }

    private async void ExportProfiles_Click(object sender, RoutedEventArgs e)
    {
        ExportProfilesButton.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop,
                SuggestedFileName = $"zia-profils-{DateTime.Now:yyyy-MM-dd}"
            };
            picker.FileTypeChoices.Add("Profils Zia Monitoring (JSON)", [".json"]);
            InitializePicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            await Task.Run(() => _app.OptimizationProfileService.ExportProfiles(file.Path));
            ProfileIoStatusLabel.Text = $"Profils exportes vers {file.Path}";
        }
        catch (Exception ex)
        {
            ProfileIoStatusLabel.Text = $"Export impossible: {ex.Message}";
            Infrastructure.AppLog.Warn("Export des profils en echec", ex);
        }
        finally
        {
            ExportProfilesButton.IsEnabled = true;
        }
    }

    private async void ImportProfiles_Click(object sender, RoutedEventArgs e)
    {
        ImportProfilesButton.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add(".json");
            InitializePicker(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var (imported, skipped) = await Task.Run(() => _app.OptimizationProfileService.ImportProfiles(file.Path));
            ProfilesList.ItemsSource = _app.OptimizationProfileService.GetProfiles();
            ProfileIoStatusLabel.Text = skipped > 0
                ? $"{imported} profil(s) importe(s), {skipped} ignore(s) (nom reserve ou invalide)."
                : $"{imported} profil(s) importe(s).";
        }
        catch (Exception ex)
        {
            ProfileIoStatusLabel.Text = $"Import impossible: {ex.Message}";
            Infrastructure.AppLog.Warn("Import des profils en echec", ex);
        }
        finally
        {
            ImportProfilesButton.IsEnabled = true;
        }
    }

    private static void InitializePicker(object picker)
    {
        // En app WinUI 3 non packagée, les pickers doivent être rattachés au HWND.
        var window = ((App)Microsoft.UI.Xaml.Application.Current).MainWindowInstance;
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }
    }

    private async void EstimateCache_Click(object sender, RoutedEventArgs e)
    {
        EstimateCacheButton.IsEnabled = false;
        CacheSizeLabel.Text = "Estimation en cours...";
        try
        {
            var sizes = await Task.Run(() => _app.BrowserCacheCleaner.EstimateSizes());
            var lines = sizes
                .Where(s => s.Exists)
                .Select(s => $"{s.Browser}: {s.TotalBytes / 1024.0 / 1024.0:F1} MB");
            CacheSizeLabel.Text = sizes.Any(s => s.Exists)
                ? string.Join("   |   ", lines)
                : "Aucun navigateur detecte.";
        }
        catch (Exception ex)
        {
            CacheSizeLabel.Text = $"Erreur: {ex.Message}";
        }
        finally
        {
            EstimateCacheButton.IsEnabled = true;
        }
    }

    private async void CleanCache_Click(object sender, RoutedEventArgs e)
    {
        CleanCacheButton.IsEnabled = false;
        CacheCleanLabel.Text = "Nettoyage en cours... (fermez les navigateurs d'abord)";
        try
        {
            var results = await Task.Run(() => _app.BrowserCacheCleaner.Clean());
            var lines = results.Select(r =>
                r.Error is null
                    ? $"{r.Browser}: {r.DeletedFiles} fichier(s) - {r.FreedBytes / 1024.0 / 1024.0:F1} MB liberes"
                    : $"{r.Browser}: erreur ({r.Error})");
            CacheCleanLabel.Text = string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            CacheCleanLabel.Text = $"Erreur: {ex.Message}";
        }
        finally
        {
            CleanCacheButton.IsEnabled = true;
        }
    }

    private async Task ShowDialog(string title, string content)
    {
        var d = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await d.ShowAsync();
    }
}
