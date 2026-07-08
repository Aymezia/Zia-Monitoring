using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;

namespace ZiaMonitoring_App.Pages;

public sealed partial class MaintenancePage : Page
{
    private readonly App _app;
    private string? _selectedFolder;
    private CancellationTokenSource? _healthCheckCts;

    public MaintenancePage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            ForecastDriveCombo.Items.Add(new ComboBoxItem { Content = drive.Name });
        ForecastDriveCombo.SelectedIndex = 0;

        AverageGameSizeSlider.Value = 90;
        AverageGameSizeLabel.Text = "90 Go";
        UpdateForecast();

        PageFileLabel.Text = "Cliquez sur 'Analyser' pour lire la configuration actuelle.";
        ShaderCacheStatusLabel.Text = "Cliquez sur 'Analyser' pour lister les caches détectés.";
        FilesStatusLabel.Text = "Choisissez un dossier, puis cherchez les doublons ou les plus gros éléments.";
        HealthCheckStatusLabel.Text = "Vérifie et répare les fichiers système Windows corrompus (DISM + SFC). Peut prendre 5 à 15 minutes.";
        ServiceDependencyLabel.Text = "Entrez le nom technique d'un service (visible dans services.msc).";
    }

    private void Forecast_Changed(object sender, object e) => UpdateForecast();

    private void UpdateForecast()
    {
        if (ForecastDriveCombo.SelectedItem is not ComboBoxItem { Content: string drive })
            return;

        var averageSize = AverageGameSizeSlider.Value;
        AverageGameSizeLabel.Text = $"{averageSize:F0} Go";

        var forecast = DiskMaintenanceService.ForecastRemainingGameSlots(drive, averageSize);
        ForecastLabel.Text = forecast.Label;
    }

    private async void ScanShaderCache_Click(object sender, RoutedEventArgs e)
    {
        ScanShaderCacheButton.IsEnabled = false;
        ShaderCacheStatusLabel.Text = "Analyse en cours…";
        try
        {
            var entries = await Task.Run(DiskMaintenanceService.ScanShaderCaches);
            ShaderCacheList.ItemsSource = entries;
            var totalMb = entries.Sum(e => e.SizeMb);
            ShaderCacheStatusLabel.Text = entries.Count == 0
                ? "Aucun cache shader détecté."
                : $"{entries.Count} cache(s) trouvé(s), {totalMb / 1024:F1} Go au total.";
        }
        finally
        {
            ScanShaderCacheButton.IsEnabled = true;
        }
    }

    private async void CleanShaderCache_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ShaderCacheEntry entry })
            return;

        ShaderCacheStatusLabel.Text = $"Vidage de '{entry.Label}'…";
        var (success, message) = await Task.Run(() => DiskMaintenanceService.CleanShaderCache(entry.Path));
        ShaderCacheStatusLabel.Text = message;
        if (success)
            ScanShaderCache_Click(sender, e);
    }

    private async void AnalyzePageFile_Click(object sender, RoutedEventArgs e)
    {
        AnalyzePageFileButton.IsEnabled = false;
        PageFileLabel.Text = "Analyse en cours…";
        try
        {
            var info = await Task.Run(DiskMaintenanceService.AnalyzePageFile);
            PageFileLabel.Text = info.Recommendation;
        }
        finally
        {
            AnalyzePageFileButton.IsEnabled = true;
        }
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add("*");
        InitializePicker(picker);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
            return;

        _selectedFolder = folder.Path;
        SelectedFolderLabel.Text = folder.Path;
        FindDuplicatesButton.IsEnabled = true;
        FindLargestButton.IsEnabled = true;
    }

    private sealed record FileRow(string Primary, string Secondary);

    private async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder is null)
            return;

        FindDuplicatesButton.IsEnabled = false;
        FilesStatusLabel.Text = "Recherche des doublons en cours (peut prendre un moment)…";
        try
        {
            var folder = _selectedFolder;
            var groups = await Task.Run(() => DiskMaintenanceService.FindDuplicates(folder));
            FilesResultsList.ItemsSource = groups
                .Take(100)
                .SelectMany(g => g.Paths.Select(p => new FileRow(p, g.Label)))
                .ToList();

            var totalWastedMb = groups.Sum(g => g.WastedMb);
            FilesStatusLabel.Text = groups.Count == 0
                ? "Aucun doublon trouvé."
                : $"{groups.Count} groupe(s) de doublons, ~{totalWastedMb:F0} Mo gaspillés.";
        }
        catch (Exception ex)
        {
            FilesStatusLabel.Text = "Recherche impossible (dossier inaccessible ?).";
            Infrastructure.AppLog.Warn("Recherche de doublons en échec", ex);
        }
        finally
        {
            FindDuplicatesButton.IsEnabled = true;
        }
    }

    private async void FindLargest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFolder is null)
            return;

        FindLargestButton.IsEnabled = false;
        FilesStatusLabel.Text = "Analyse en cours…";
        try
        {
            var folder = _selectedFolder;
            var (files, folders) = await Task.Run(() => DiskMaintenanceService.FindLargestItems(folder));

            var rows = folders.Select(f => new FileRow($"[Dossier] {f.Path}", $"{f.SizeMb / 1024:F1} Go"))
                .Concat(files.Select(f => new FileRow(f.Path, $"{f.SizeMb:F0} Mo")))
                .ToList();

            FilesResultsList.ItemsSource = rows;
            FilesStatusLabel.Text = $"{folders.Count} sous-dossier(s), {files.Count} fichier(s) analysé(s).";
        }
        catch (Exception ex)
        {
            FilesStatusLabel.Text = "Analyse impossible (dossier inaccessible ?).";
            Infrastructure.AppLog.Warn("Recherche des plus gros fichiers en échec", ex);
        }
        finally
        {
            FindLargestButton.IsEnabled = true;
        }
    }

    private async void RunHealthCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_healthCheckCts is not null)
        {
            _healthCheckCts.Cancel();
            return;
        }

        _healthCheckCts = new CancellationTokenSource();
        RunHealthCheckButton.Content = "Annuler";
        HealthCheckStatusLabel.Text = "Analyse en cours (fenêtre UAC possible pour DISM/SFC)…";
        var log = new StringBuilder();
        var dispatcher = DispatcherQueue;

        try
        {
            var (success, summary) = await _app.SystemHealth.RunHealthCheckAsync(line =>
            {
                dispatcher.TryEnqueue(() =>
                {
                    log.AppendLine(line);
                    HealthCheckLog.Text = log.ToString();
                });
            }, _healthCheckCts.Token);

            HealthCheckStatusLabel.Text = summary;
        }
        finally
        {
            _healthCheckCts.Dispose();
            _healthCheckCts = null;
            RunHealthCheckButton.Content = "Lancer (plusieurs minutes)";
        }
    }

    private void InspectService_Click(object sender, RoutedEventArgs e)
    {
        var name = ServiceNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        var info = SystemHealthService.GetServiceDependencies(name);
        ServiceDependencyLabel.Text = info.Label;
    }

    private void RefreshBootHistory_Click(object sender, RoutedEventArgs e)
    {
        BootHistoryList.ItemsSource = SystemHealthService.GetBootTimeHistory();
    }

    private static void InitializePicker(object picker)
    {
        var window = ((App)Microsoft.UI.Xaml.Application.Current).MainWindowInstance;
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }
    }
}
