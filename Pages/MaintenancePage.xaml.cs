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

        LoadStorageSense();
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

        if (!await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "L'analyse DISM/SFC"))
            return;

        _healthCheckCts = new CancellationTokenSource();
        RunHealthCheckButton.Content = "Annuler";
        HealthCheckStatusLabel.Text = "Analyse en cours…";
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

    private async void RefreshStability_Click(object sender, RoutedEventArgs e)
    {
        RefreshStabilityButton.IsEnabled = false;
        BsodLabel.Text = "Analyse des journaux Windows en cours…";
        try
        {
            var diag = _app.CrashDiagnostics;
            var (crashes, whea, bsod, memDiag, serviceLoops) = await Task.Run(() =>
                (diag.GetRecentAppCrashes(), diag.GetWheaSummary(), diag.GetLastBsod(), diag.GetLastMemoryDiagnosticResult(), diag.GetServiceCrashLoops()));

            BsodLabel.Text = bsod?.Label ?? "Aucun écran bleu journalisé.";
            WheaLabel.Text = whea.Label;
            CrashGroupsList.ItemsSource = crashes;
            MemDiagLabel.Text = memDiag ?? "Aucun diagnostic mémoire exécuté (le verdict apparaît ici après le test).";
            ServiceCrashLoopList.ItemsSource = serviceLoops;
            ServiceCrashLoopStatusLabel.Text = serviceLoops.Count == 0
                ? "Aucun service en boucle de redémarrage sur la dernière heure."
                : $"{serviceLoops.Count} service(s) en boucle de redémarrage détecté(s).";
        }
        finally
        {
            RefreshStabilityButton.IsEnabled = true;
        }
    }

    private void LaunchMemDiag_Click(object sender, RoutedEventArgs e)
    {
        var (_, message) = CrashDiagnosticsService.LaunchMemoryDiagnostic();
        MemDiagLabel.Text = message;
    }

    private void RefreshLeaks_Click(object sender, RoutedEventArgs e)
    {
        var suspects = _app.MonitoringService.MemoryLeakSuspects;
        LeaksList.ItemsSource = suspects;
        LeaksStatusLabel.Text = suspects.Count == 0
            ? "Aucune fuite suspectée pour l'instant (au moins 1 h d'observation nécessaire)."
            : $"{suspects.Count} process suspect(s).";

        var drift = _app.MetricsHistory.GetThermalDriftWarnings();
        ThermalDriftLabel.Text = drift.Count == 0
            ? "Dérive thermique : rien à signaler (ou pas encore assez d'historique)."
            : string.Join(Environment.NewLine, drift);
    }

    private async void RefreshUpdates_Click(object sender, RoutedEventArgs e)
    {
        RefreshUpdatesButton.IsEnabled = false;
        UpdatesStatusLabel.Text = "Lecture en cours…";
        try
        {
            var updates = await Task.Run(() => _app.WindowsUpdates.GetInstalledUpdates());
            UpdatesList.ItemsSource = updates;
            UpdatesStatusLabel.Text = $"{updates.Count} mise(s) à jour installée(s), la plus récente en premier.";
        }
        finally
        {
            RefreshUpdatesButton.IsEnabled = true;
        }
    }

    private void UninstallKb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hotFixId })
            return;

        var (_, message) = _app.WindowsUpdates.UninstallKb(hotFixId);
        UpdatesStatusLabel.Text = message;
    }

    private async void RefreshDiskOptimization_Click(object sender, RoutedEventArgs e)
    {
        RefreshDiskOptimizationButton.IsEnabled = false;
        TrimStatusLabel.Text = "Analyse en cours…";
        try
        {
            var (trim, scheduledEnabled, disks) = await Task.Run(() =>
                (TrimStatusService.GetStatus(), DiskOptimizationAuditService.IsScheduledOptimizationEnabled(), DiskOptimizationAuditService.GetDiskMediaTypes()));

            TrimStatusLabel.Text = trim?.Label ?? "Statut TRIM illisible sur ce système.";
            ScheduledOptimizationLabel.Text = scheduledEnabled
                ? "Optimisation planifiée Windows (TRIM + défrag) : active."
                : "Optimisation planifiée Windows désactivée : ni TRIM ni défragmentation ne s'exécutent automatiquement.";
            DiskMediaTypeList.ItemsSource = disks;
        }
        finally
        {
            RefreshDiskOptimizationButton.IsEnabled = true;
        }
    }

    private async void RefreshAdvancedCleanup_Click(object sender, RoutedEventArgs e)
    {
        RefreshAdvancedCleanupButton.IsEnabled = false;
        AdvancedCleanupStatusLabel.Text = "Analyse en cours…";
        try
        {
            var (targets, installers) = await Task.Run(() =>
                (_app.AdvancedCleanup.Scan(), _app.AdvancedCleanup.ScanOldInstallers()));

            AdvancedCleanupList.ItemsSource = targets;
            OldInstallersList.ItemsSource = installers;
            var total = targets.Where(t => t.Available).Sum(t => t.SizeMb) + installers.Sum(i => i.SizeMb);
            AdvancedCleanupStatusLabel.Text = $"~{total / 1024:F1} Go potentiellement récupérables ({installers.Count} vieux installeur(s)).";
        }
        finally
        {
            RefreshAdvancedCleanupButton.IsEnabled = true;
        }
    }

    private async void RunAdvancedCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id })
            return;

        if (!await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "Ce nettoyage disque"))
            return;

        AdvancedCleanupStatusLabel.Text = "Nettoyage en cours…";
        var (_, message) = await Task.Run(() => _app.AdvancedCleanup.Clean(id));
        AdvancedCleanupStatusLabel.Text = message;
        RefreshAdvancedCleanup_Click(sender, e);
    }

    private void DeleteInstaller_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string path })
            return;

        var (_, message) = _app.AdvancedCleanup.DeleteInstaller(path);
        AdvancedCleanupStatusLabel.Text = message;
        OldInstallersList.ItemsSource = _app.AdvancedCleanup.ScanOldInstallers();
    }

    private async void ComponentCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (!await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "Le nettoyage des composants Windows"))
            return;

        ComponentCleanupButton.IsEnabled = false;
        ComponentCleanupLabel.Text = "Nettoyage WinSxS en cours (plusieurs minutes)…";
        var dispatcher = DispatcherQueue;
        try
        {
            var (_, summary) = await _app.SystemHealth.RunComponentCleanupAsync(line =>
                dispatcher.TryEnqueue(() => ComponentCleanupLabel.Text = line), CancellationToken.None);
            ComponentCleanupLabel.Text = summary;
        }
        finally
        {
            ComponentCleanupButton.IsEnabled = true;
        }
    }

    private async void RefreshRestorePoints_Click(object sender, RoutedEventArgs e)
    {
        var usage = await Task.Run(ZiaMonitoring_App.Application.AdvancedCleanupService.GetRestorePointsUsage);
        RestorePointsLabel.Text = usage.Label;
    }

    private async void PurgeRestorePoints_Click(object sender, RoutedEventArgs e)
    {
        if (!await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "La purge des points de restauration"))
            return;

        RestorePointsLabel.Text = "Purge en cours…";
        var (_, message) = await Task.Run(() => _app.AdvancedCleanup.PurgeOldRestorePoints());
        RestorePointsLabel.Text = message;
    }

    private void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        var (_, message) = ZiaMonitoring_App.Application.SystemHygieneService.RestartExplorer();
        HygieneStatusLabel.Text = message;
    }

    private void RepairIconCache_Click(object sender, RoutedEventArgs e)
    {
        var (_, message) = ZiaMonitoring_App.Application.SystemHygieneService.RepairIconCache();
        HygieneStatusLabel.Text = message;
    }

    private void OpenIndexingOptions_Click(object sender, RoutedEventArgs e)
    {
        var (_, message) = ZiaMonitoring_App.Application.SystemHygieneService.OpenIndexingOptions();
        HygieneStatusLabel.Text = message;
    }

    private async void RefreshContextMenu_Click(object sender, RoutedEventArgs e)
    {
        RefreshContextMenuButton.IsEnabled = false;
        try
        {
            var handlers = await Task.Run(ZiaMonitoring_App.Application.SystemHygieneService.ScanContextMenuHandlers);
            ContextMenuList.ItemsSource = handlers;
            HygieneStatusLabel.Text = $"{handlers.Count} gestionnaire(s) de menu contextuel tiers.";
        }
        finally
        {
            RefreshContextMenuButton.IsEnabled = true;
        }
    }

    private void RefreshPendingReboot_Click(object sender, RoutedEventArgs e)
    {
        PendingRebootLabel.Text = _app.PendingReboot.GetStatus().Label;
    }

    private bool _storageSenseLoading;

    private void LoadStorageSense()
    {
        _storageSenseLoading = true;
        try
        {
            var config = _app.StorageSense.GetConfig();
            StorageSenseToggle.IsOn = config.Enabled;
            StorageSenseCadenceCombo.SelectedIndex = config.Cadence switch { 1 => 1, 7 => 2, 30 => 3, _ => 0 };
            StorageSenseRecycleCombo.SelectedIndex = config.RecycleBinDays switch { 14 => 1, 30 => 2, 60 => 3, _ => 0 };
            StorageSenseDownloadsCombo.SelectedIndex = config.DownloadsDays switch { 14 => 1, 30 => 2, 60 => 3, _ => 0 };
        }
        finally
        {
            _storageSenseLoading = false;
        }
    }

    private void StorageSense_Toggled(object sender, RoutedEventArgs e) => SaveStorageSense();
    private void StorageSenseCadence_Changed(object sender, SelectionChangedEventArgs e) => SaveStorageSense();
    private void StorageSenseRetention_Changed(object sender, SelectionChangedEventArgs e) => SaveStorageSense();

    private void SaveStorageSense()
    {
        if (_storageSenseLoading)
            return;

        var config = new ZiaMonitoring_App.Application.StorageSenseConfig(
            Enabled: StorageSenseToggle.IsOn,
            Cadence: TagInt(StorageSenseCadenceCombo),
            CleanTemp: true,
            RecycleBinDays: TagInt(StorageSenseRecycleCombo),
            DownloadsDays: TagInt(StorageSenseDownloadsCombo));

        var (_, message) = _app.StorageSense.Apply(config);
        StorageSenseStatusLabel.Text = message;
    }

    private static int TagInt(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var v) ? v : 0;

    private async void RunLatencyProbe_Click(object sender, RoutedEventArgs e)
    {
        RunLatencyProbeButton.IsEnabled = false;
        LatencyProbeLabel.Text = "Mesure en cours (3 s)…";
        try
        {
            var result = await ZiaMonitoring_App.Application.DpcLatencyProbe.MeasureAsync();
            LatencyProbeLabel.Text = result.Label;
        }
        finally
        {
            RunLatencyProbeButton.IsEnabled = true;
        }
    }

    private async void ExportWarrantyReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop,
                SuggestedFileName = $"zia-historique-garantie-{DateTime.Now:yyyy-MM-dd}"
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            InitializePicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            var rowCount = await Task.Run(() => _app.MetricsHistory.ExportWarrantyReportToCsv(file.Path));
            WarrantyExportStatusLabel.Text = rowCount == 0
                ? $"Export terminé, mais aucune donnée disponible pour l'instant (revenez après quelques jours d'utilisation) : {file.Path}"
                : $"{rowCount} ligne(s) exportée(s) vers {file.Path}";
        }
        catch (Exception ex)
        {
            WarrantyExportStatusLabel.Text = $"Export impossible : {ex.Message}";
            Infrastructure.AppLog.Warn("Export de l'historique garantie en échec", ex);
        }
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
