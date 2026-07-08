// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure;

namespace ZiaMonitoring_App.Pages;

public sealed partial class SettingsPage : Page
{
    private readonly App _app;
    private AppSettings _settings = new(1, true, true, false, false, TimeSpan.FromHours(3), "dark", true, true);
    private bool _loading = true;
    private readonly List<string> _pendingCompanionApps = [];
    private readonly List<string> _pendingKillList = [];

    public SettingsPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;

        try
        {
            _settings = _app.SettingsService.Load();

            RefreshSlider.Value = _settings.RefreshIntervalSeconds;
            RefreshValueLabel.Text = $"{_settings.RefreshIntervalSeconds}s";
            ToastToggle.IsOn = _settings.EnableToastAlerts;
            DailySummaryToggle.IsOn = _settings.EnableDailyHealthSummary;
            SystrayToggle.IsOn = _settings.ShowSystray;
            AutoStartToggle.IsOn = AutoStartManager.IsEnabled();
            HotkeyToggle.IsOn = _settings.EnableGlobalHotkey;
            OverlayToggle.IsOn = _settings.EnableGameOverlay;
            MiniWidgetToggle.IsOn = _settings.EnableMiniWidget;
            MiniOpacitySlider.Value = _settings.MiniWidgetOpacity * 100;
            MiniOpacityValueLabel.Text = $"{MiniOpacitySlider.Value:F0}%";
            WidgetThemeCombo.SelectedIndex = (int)_settings.WidgetTheme;

            foreach (var name in PerformanceOverlayWindow.GetMonitorNames())
                OverlayMonitorCombo.Items.Add(new ComboBoxItem { Content = name });
            OverlayMonitorCombo.SelectedIndex = Math.Clamp(_settings.OverlayMonitorIndex, 0, Math.Max(0, OverlayMonitorCombo.Items.Count - 1));
            OverlayCornerCombo.SelectedIndex = (int)_settings.OverlayPosition;
            GameBoosterToggle.IsOn = _settings.EnableGameBooster;
            RestorePointToggle.IsOn = _settings.EnableRestorePointBeforeRiskyActions;
            SilentModeToggle.IsOn = _settings.AutoSilentModeOnGame;
            SchedulerToggle.IsOn = _settings.EnableCleanupScheduler;
            SaveBackupToggle.IsOn = _settings.EnableScheduledSaveBackup;
            PrometheusToggle.IsOn = _settings.EnablePrometheusExporter;
            PrometheusStatusLabel.Text = _app.PrometheusExporter.IsRunning
                ? $"Actif : http://localhost:{_app.PrometheusExporter.Port}/metrics"
                : "Inactif.";

            ObsToggle.IsOn = _settings.EnableObsAutoSceneSwitch;
            ObsHostBox.Text = _settings.ObsHost;
            ObsPortBox.Text = _settings.ObsPort.ToString();
            ObsPasswordBox.Password = _settings.ObsPassword;
            ObsGameSceneBox.Text = _settings.ObsGameSceneName;
            ObsIdleSceneBox.Text = _settings.ObsIdleSceneName;

            HardwareSensorsToggle.IsOn = _settings.EnableHardwareSensors;

            GameLaunchProfilesToggle.IsOn = _settings.EnableGameLaunchProfiles;
            ClipboardClearToggle.IsOn = _settings.EnableClipboardClearOnGameLaunch;
            RefreshSavedProfiles();

            CpuAlertSlider.Value = _settings.CpuAlertThresholdPercent;
            CpuAlertValueLabel.Text = $"{_settings.CpuAlertThresholdPercent:F0}%";
            CpuTempAlertSlider.Value = _settings.CpuTempAlertThresholdC;
            CpuTempAlertValueLabel.Text = $"{_settings.CpuTempAlertThresholdC:F0}°C";
            GpuTempAlertSlider.Value = _settings.GpuTempAlertThresholdC;
            GpuTempAlertValueLabel.Text = $"{_settings.GpuTempAlertThresholdC:F0}°C";
            DiskAlertSlider.Value = _settings.DiskFreeAlertGb;
            DiskAlertValueLabel.Text = $"{_settings.DiskFreeAlertGb:F0} GB";
            PlaytimeGoalSlider.Value = _settings.WeeklyPlaytimeGoalHours;
            PlaytimeGoalValueLabel.Text = _settings.WeeklyPlaytimeGoalHours > 0 ? $"{_settings.WeeklyPlaytimeGoalHours:F0} h" : "Désactivé";

            _loading = false;
            _settings = _settings with { EnableAutoStart = AutoStartToggle.IsOn };

            RefreshHistory_Click(this, null!);
            RefreshCustomRules();
        }
        catch (Exception ex)
        {
            _loading = false;
            Infrastructure.AppLog.Warn("Chargement de la page Parametres en echec", ex);
        }
    }

    private void SaveSettings()
    {
        if (_loading)
            return;
        _app.SettingsService.Save(_settings);
    }

    private void RefreshSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var val = (int)e.NewValue;
        if (RefreshValueLabel != null)
            RefreshValueLabel.Text = $"{val}s";
        _settings = _settings with { RefreshIntervalSeconds = val };
        SaveSettings();
    }

    private void ToastToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableToastAlerts = ToastToggle.IsOn };
        SaveSettings();
    }

    private void DailySummary_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableDailyHealthSummary = DailySummaryToggle.IsOn };
        SaveSettings();
    }

    private void Systray_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { ShowSystray = SystrayToggle.IsOn };
        SaveSettings();
    }

    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableAutoStart = AutoStartToggle.IsOn };
        if (!_loading)
        {
            if (AutoStartToggle.IsOn)
                AutoStartManager.Enable();
            else
                AutoStartManager.Disable();
        }

        SaveSettings();
    }

    private void Hotkey_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableGlobalHotkey = HotkeyToggle.IsOn };
        SaveSettings();
    }

    private void Overlay_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableGameOverlay = OverlayToggle.IsOn };
        SaveSettings();
    }

    private void MiniWidget_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableMiniWidget = MiniWidgetToggle.IsOn };
        SaveSettings();
    }

    private void OverlayPosition_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || OverlayMonitorCombo.SelectedIndex < 0 || OverlayCornerCombo.SelectedIndex < 0)
            return;

        _settings = _settings with
        {
            OverlayMonitorIndex = OverlayMonitorCombo.SelectedIndex,
            OverlayPosition = (OverlayCorner)OverlayCornerCombo.SelectedIndex
        };
        SaveSettings();
    }

    private void MiniOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MiniOpacityValueLabel != null)
            MiniOpacityValueLabel.Text = $"{e.NewValue:F0}%";

        _settings = _settings with { MiniWidgetOpacity = Math.Clamp(e.NewValue / 100.0, 0.35, 1.0) };
        SaveSettings();
    }

    private void WidgetTheme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || WidgetThemeCombo.SelectedIndex < 0)
            return;

        _settings = _settings with { WidgetTheme = (WidgetTheme)WidgetThemeCombo.SelectedIndex };
        SaveSettings();
        _app.Achievements.Increment("widget_theme_changes");
    }

    private void CpuAlertSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (CpuAlertValueLabel != null)
            CpuAlertValueLabel.Text = $"{e.NewValue:F0}%";
        _settings = _settings with { CpuAlertThresholdPercent = e.NewValue };
        SaveSettings();
    }

    private void CpuTempAlertSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (CpuTempAlertValueLabel != null)
            CpuTempAlertValueLabel.Text = $"{e.NewValue:F0}°C";
        _settings = _settings with { CpuTempAlertThresholdC = e.NewValue };
        SaveSettings();
    }

    private void GpuTempAlertSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (GpuTempAlertValueLabel != null)
            GpuTempAlertValueLabel.Text = $"{e.NewValue:F0}°C";
        _settings = _settings with { GpuTempAlertThresholdC = e.NewValue };
        SaveSettings();
    }

    private void DiskAlertSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (DiskAlertValueLabel != null)
            DiskAlertValueLabel.Text = $"{e.NewValue:F0} GB";
        _settings = _settings with { DiskFreeAlertGb = e.NewValue };
        SaveSettings();
    }

    private void PlaytimeGoalSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (PlaytimeGoalValueLabel != null)
            PlaytimeGoalValueLabel.Text = e.NewValue > 0 ? $"{e.NewValue:F0} h" : "Désactivé";
        _settings = _settings with { WeeklyPlaytimeGoalHours = e.NewValue };
        SaveSettings();
    }

    private void SilentMode_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { AutoSilentModeOnGame = SilentModeToggle.IsOn };
        SaveSettings();
    }

    private void GameBooster_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableGameBooster = GameBoosterToggle.IsOn };
        SaveSettings();
    }

    private void HardwareSensors_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableHardwareSensors = HardwareSensorsToggle.IsOn };
        SaveSettings();
    }

    private void GameLaunchProfiles_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableGameLaunchProfiles = GameLaunchProfilesToggle.IsOn };
        SaveSettings();
    }

    private void ClipboardClear_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableClipboardClearOnGameLaunch = ClipboardClearToggle.IsOn };
        SaveSettings();
    }

    private async void AddCompanionApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
        };
        picker.FileTypeFilter.Add(".exe");
        var window = _app.MainWindowInstance;
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        _pendingCompanionApps.Add(file.Path);
        PendingCompanionAppsList.ItemsSource = null;
        PendingCompanionAppsList.ItemsSource = _pendingCompanionApps.ToList();
    }

    private void RemoveCompanionApp_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            _pendingCompanionApps.Remove(path);

        PendingCompanionAppsList.ItemsSource = null;
        PendingCompanionAppsList.ItemsSource = _pendingCompanionApps.ToList();
    }

    private void AddKillProcess_Click(object sender, RoutedEventArgs e)
    {
        var name = KillProcessBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        _pendingKillList.Add(name);
        KillProcessBox.Text = string.Empty;
        PendingKillList.ItemsSource = null;
        PendingKillList.ItemsSource = _pendingKillList.ToList();
    }

    private void RemoveKillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
            _pendingKillList.Remove(name);

        PendingKillList.ItemsSource = null;
        PendingKillList.ItemsSource = _pendingKillList.ToList();
    }

    private void SaveGameProfile_Click(object sender, RoutedEventArgs e)
    {
        var gameName = ProfileGameNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(gameName))
        {
            GameProfileStatusLabel.Text = "Entrez le nom du process du jeu.";
            return;
        }

        if (_pendingCompanionApps.Count == 0 && _pendingKillList.Count == 0)
        {
            GameProfileStatusLabel.Text = "Ajoutez au moins une appli compagnon ou un process à tuer.";
            return;
        }

        _app.GameLaunchProfiles.SaveProfile(new GameLaunchProfile(gameName, _pendingCompanionApps.ToList(), _pendingKillList.ToList()));

        ProfileGameNameBox.Text = string.Empty;
        _pendingCompanionApps.Clear();
        _pendingKillList.Clear();
        PendingCompanionAppsList.ItemsSource = null;
        PendingKillList.ItemsSource = null;
        GameProfileStatusLabel.Text = $"Profil '{gameName}' enregistré.";
        RefreshSavedProfiles();
    }

    private void DeleteGameProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string gameName })
            return;

        _app.GameLaunchProfiles.DeleteProfile(gameName);
        RefreshSavedProfiles();
    }

    private void RefreshSavedProfiles()
    {
        SavedProfilesList.ItemsSource = _app.GameLaunchProfiles.GetProfiles();
    }

    private void RestorePoint_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableRestorePointBeforeRiskyActions = RestorePointToggle.IsOn };
        SaveSettings();
    }

    private void Prometheus_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnablePrometheusExporter = PrometheusToggle.IsOn };
        SaveSettings();

        if (PrometheusToggle.IsOn)
        {
            var (success, message) = _app.PrometheusExporter.Start();
            PrometheusStatusLabel.Text = message;
            if (!success)
            {
                PrometheusToggle.IsOn = false;
                _settings = _settings with { EnablePrometheusExporter = false };
                SaveSettings();
            }
        }
        else
        {
            _app.PrometheusExporter.Stop();
            PrometheusStatusLabel.Text = "Inactif.";
        }
    }

    private void Obs_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableObsAutoSceneSwitch = ObsToggle.IsOn };
        SaveSettings();
    }

    private void ObsFields_Changed(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;

        _ = int.TryParse(ObsPortBox.Text, out var port);
        _settings = _settings with
        {
            ObsHost = string.IsNullOrWhiteSpace(ObsHostBox.Text) ? "localhost" : ObsHostBox.Text.Trim(),
            ObsPort = port > 0 ? port : 4455,
            ObsGameSceneName = ObsGameSceneBox.Text.Trim(),
            ObsIdleSceneName = ObsIdleSceneBox.Text.Trim()
        };
        SaveSettings();
    }

    private void ObsPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _settings = _settings with { ObsPassword = ObsPasswordBox.Password };
        SaveSettings();
    }

    private async void TestObsConnection_Click(object sender, RoutedEventArgs e)
    {
        ObsStatusLabel.Text = "Connexion en cours…";
        try
        {
            var (success, message) = await _app.ObsWebSocket.ConnectAsync(_settings.ObsHost, _settings.ObsPort, _settings.ObsPassword);
            ObsStatusLabel.Text = message;
        }
        catch (Exception ex)
        {
            ObsStatusLabel.Text = $"Échec : {ex.Message}";
            AppLog.Warn("Test de connexion OBS en échec", ex);
        }
    }

    private void RefreshCustomRules() => CustomRulesList.ItemsSource = _app.CustomRules.GetRules();

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(RuleNameBox.Text) ? "Règle" : RuleNameBox.Text.Trim();
        var conditionTag = (RuleConditionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "CpuAbove";
        var actionTag = (RuleActionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Notify";

        if (!double.TryParse(RuleThresholdBox.Text, out var threshold))
        {
            RuleStatusLabel.Text = "Seuil invalide.";
            return;
        }

        if (!int.TryParse(RuleMinutesBox.Text, out var minutes))
        {
            RuleStatusLabel.Text = "Durée (min) invalide.";
            return;
        }

        var condition = Enum.Parse<ZiaMonitoring_App.Application.RuleCondition>(conditionTag);
        var action = Enum.Parse<ZiaMonitoring_App.Application.RuleAction>(actionTag);

        _app.CustomRules.AddRule(name, condition, threshold, minutes, action);
        _app.Achievements.Increment("custom_rules_created");
        RuleNameBox.Text = string.Empty;
        RuleThresholdBox.Text = string.Empty;
        RuleMinutesBox.Text = string.Empty;
        RuleStatusLabel.Text = "Règle ajoutée.";
        RefreshCustomRules();
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id })
        {
            _app.CustomRules.RemoveRule(id);
            RefreshCustomRules();
        }
    }

    private void Scheduler_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableCleanupScheduler = SchedulerToggle.IsOn };
        SaveSettings();
    }

    private void SaveBackup_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableScheduledSaveBackup = SaveBackupToggle.IsOn };
        SaveSettings();
    }

    private async void BackupNow_Click(object sender, RoutedEventArgs e)
    {
        BackupNowButton.IsEnabled = false;
        BackupStatusLabel.Text = "Sauvegarde en cours…";
        try
        {
            var result = await Task.Run(() => _app.SaveBackup.BackupNow());
            _app.Achievements.Increment("save_backups");
            BackupStatusLabel.Text = result.Warnings.Count == 0
                ? result.Summary
                : $"{result.Summary} ({result.Warnings.Count} avertissement(s))";
        }
        catch (Exception ex)
        {
            BackupStatusLabel.Text = $"Sauvegarde impossible : {ex.Message}";
            AppLog.Warn("Sauvegarde manuelle des saves en échec", ex);
        }
        finally
        {
            BackupNowButton.IsEnabled = true;
        }
    }

    private void DisableAnimations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SilentModeService.SetWindowsAnimations(false);
        }
        catch (Exception ex)
        {
            ShowDialog("Erreur", ex.Message);
        }
    }

    private void RestoreAnimations_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SilentModeService.SetWindowsAnimations(true);
        }
        catch (Exception ex)
        {
            ShowDialog("Erreur", ex.Message);
        }
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var state = _app.State;
            var html = BuildHtmlReport(state);
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"ZiaMonitoring-Report-{DateTime.Now:yyyy-MM-dd_HHmm}.html");
            File.WriteAllText(path, html);

            ExportStatusLabel.Text = $"Rapport enregistre: {path}";

            var psi = new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            ExportStatusLabel.Text = $"Erreur: {ex.Message}";
        }
    }

    private void RefreshHistory_Click(object sender, RoutedEventArgs e)
    {
        var history = _app.BoostHistoryService.Load()
            .Select(h => new BoostHistoryRow(h))
            .ToList();
        HistoryList.ItemsSource = history;
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        ExportCsvButton.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop,
                SuggestedFileName = $"zia-historique-{DateTime.Now:yyyy-MM-dd}"
            };
            picker.FileTypeChoices.Add("CSV", [".csv"]);
            InitializeExportPicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            var count = await Task.Run(() => _app.MetricsHistory.ExportToCsv(file.Path));
            ExportHistoryStatusLabel.Text = $"{count} échantillon(s) exporté(s) vers {file.Path}";
        }
        catch (Exception ex)
        {
            ExportHistoryStatusLabel.Text = $"Export impossible : {ex.Message}";
            AppLog.Warn("Export CSV de l'historique en échec", ex);
        }
        finally
        {
            ExportCsvButton.IsEnabled = true;
        }
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        ExportJsonButton.IsEnabled = false;
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop,
                SuggestedFileName = $"zia-historique-{DateTime.Now:yyyy-MM-dd}"
            };
            picker.FileTypeChoices.Add("JSON", [".json"]);
            InitializeExportPicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            var count = await Task.Run(() => _app.MetricsHistory.ExportToJson(file.Path));
            ExportHistoryStatusLabel.Text = $"{count} échantillon(s) exporté(s) vers {file.Path}";
        }
        catch (Exception ex)
        {
            ExportHistoryStatusLabel.Text = $"Export impossible : {ex.Message}";
            AppLog.Warn("Export JSON de l'historique en échec", ex);
        }
        finally
        {
            ExportJsonButton.IsEnabled = true;
        }
    }

    private static void InitializeExportPicker(object picker)
    {
        var window = ((App)Microsoft.UI.Xaml.Application.Current).MainWindowInstance;
        if (window is not null)
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }
    }

    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop,
                SuggestedFileName = $"zia-reglages-{DateTime.Now:yyyy-MM-dd}"
            };
            picker.FileTypeChoices.Add("Réglages Zia Monitoring (JSON)", [".json"]);
            InitializeExportPicker(picker);

            var file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            _app.SettingsService.ExportToFile(file.Path);
            SettingsBackupStatusLabel.Text = $"Réglages exportés vers {file.Path}";
        }
        catch (Exception ex)
        {
            SettingsBackupStatusLabel.Text = $"Export impossible : {ex.Message}";
            AppLog.Warn("Export complet des réglages en échec", ex);
        }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add(".json");
            InitializeExportPicker(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            _app.SettingsService.ImportFromFile(file.Path);
            SettingsBackupStatusLabel.Text = "Réglages importés. Redémarrez l'application pour les appliquer partout.";
        }
        catch (Exception ex)
        {
            SettingsBackupStatusLabel.Text = $"Import impossible : {ex.Message}";
            AppLog.Warn("Import complet des réglages en échec", ex);
        }
    }

    private void SettingsSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var query = SettingsSearchBox.Text.Trim();
        var cards = new (Border Card, string Keywords)[]
        {
            (MonitoringCard, "monitoring intervalle rafraichissement alertes toast resume sante quotidien seuils cpu temperature gpu disque playtime temps de jeu"),
            (SystemCard, "systeme systray demarrage automatique raccourci hotkey overlay widget transparence ecran"),
            (GamesOptimizationCard, "jeux optimisation game booster point de restauration mode silencieux nettoyage temp planifie sauvegarde saves animations"),
            (ExportCard, "export rapport html"),
            (BoostHistoryCard, "historique des optimisations boost"),
            (MetricsExportCard, "export historique metriques csv json"),
            (SaveBackupCard, "sauvegarde saves de jeux my games saved games"),
            (CustomRulesCard, "regles personnalisees automatisation condition action"),
            (PrometheusCard, "export prometheus grafana metrics"),
            (ObsCard, "obs websocket streaming scene bascule"),
            (HardwareSensorsCard, "capteurs materiels temperatures ventilateurs winring0 defender"),
            (GameLaunchProfilesCard, "profils de lancement par jeu companion presse-papiers kill process"),
            (SettingsBackupCard, "sauvegarde complete des reglages export import migration")
        };

        foreach (var (card, keywords) in cards)
        {
            card.Visibility = string.IsNullOrWhiteSpace(query) || keywords.Contains(query, StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static string BuildHtmlReport(ZiaMonitoring_App.ViewModels.AppStateViewModel state)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang='fr'><head><meta charset='utf-8'/><title>Zia Monitoring - Rapport</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;background:#111;color:#eee;padding:24px}");
        sb.AppendLine("h1{color:#a855f7}h2{color:#e11d48;margin-top:24px}");
        sb.AppendLine("table{width:100%;border-collapse:collapse;margin-top:8px}");
        sb.AppendLine("td,th{border:1px solid #333;padding:8px;text-align:left}");
        sb.AppendLine("th{background:#1a1a1a;color:#a855f7}</style></head><body>");
        sb.AppendLine("<h1>Zia Monitoring - Rapport de diagnostic</h1>");
        sb.AppendLine($"<p>Genere le {DateTime.Now:dd/MM/yyyy HH:mm}</p>");
        sb.AppendLine("<h2>Machine</h2><table><tr><th>Champ</th><th>Valeur</th></tr>");
        sb.AppendLine($"<tr><td>Machine</td><td>{state.MachineName}</td></tr>");
        sb.AppendLine($"<tr><td>OS</td><td>{state.OperatingSystem}</td></tr>");
        sb.AppendLine($"<tr><td>CPU</td><td>{state.CpuModel}</td></tr>");
        sb.AppendLine($"<tr><td>GPU</td><td>{state.GpuModel}</td></tr>");
        sb.AppendLine($"<tr><td>Carte mere</td><td>{state.Motherboard}</td></tr>");
        sb.AppendLine($"<tr><td>BIOS</td><td>{state.BiosVersion}</td></tr>");
        sb.AppendLine($"<tr><td>RAM</td><td>{state.InstalledRamGb:F1} GB</td></tr>");
        sb.AppendLine($"<tr><td>Stockage total</td><td>{state.TotalDiskGb:F1} GB</td></tr>");
        sb.AppendLine($"<tr><td>{state.UptimeLabel}</td><td></td></tr></table>");
        sb.AppendLine("<h2>Etat systeme actuel</h2><table><tr><th>Metrique</th><th>Valeur</th></tr>");
        sb.AppendLine($"<tr><td>CPU</td><td>{state.CpuPercentLabel}</td></tr>");
        sb.AppendLine($"<tr><td>RAM</td><td>{state.MemoryPercentLabel}</td></tr>");
        sb.AppendLine($"<tr><td>Temp CPU</td><td>{state.CpuTempLabel}</td></tr>");
        sb.AppendLine($"<tr><td>GPU Usage</td><td>{state.GpuUsageLabel}</td></tr>");
        sb.AppendLine($"<tr><td>GPU Temp</td><td>{state.GpuTempLabel}</td></tr>");
        sb.AppendLine($"<tr><td>Reseau</td><td>{state.NetworkLabel}</td></tr>");
        sb.AppendLine($"<tr><td>Ping</td><td>{state.PingLabel}</td></tr>");
        sb.AppendLine($"<tr><td>Score sante</td><td>{state.HealthScoreLabel}</td></tr>");
        sb.AppendLine($"<tr><td>Risque</td><td>{state.RiskLevel}</td></tr></table>");
        sb.AppendLine("<h2>Disques</h2><table><tr><th>Nom</th><th>Format</th><th>Total (GB)</th><th>Libre (GB)</th></tr>");
        foreach (var d in state.Disks)
            sb.AppendLine($"<tr><td>{d.Name}</td><td>{d.Format}</td><td>{d.TotalGb:F1}</td><td>{d.FreeGb:F1}</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine($"<h2>Jeux detectes ({state.InstalledGames.Count})</h2>");
        sb.AppendLine("<table><tr><th>Nom</th><th>Plateforme</th><th>Version</th><th>Temps de jeu</th></tr>");
        foreach (var g in state.InstalledGames)
        {
            var playtime = g.PlayTime.TotalMinutes > 0
                ? $"{(int)g.PlayTime.TotalHours}h{g.PlayTime.Minutes:D2}m"
                : "N/A";
            sb.AppendLine($"<tr><td>{g.Name}</td><td>{g.Platform}</td><td>{g.Version}</td><td>{playtime}</td></tr>");
        }
        sb.AppendLine("</table><h2>Alertes actives</h2><ul>");
        foreach (var a in state.Alerts)
            sb.AppendLine($"<li style='color:#e11d48'>{a}</li>");
        sb.AppendLine("</ul></body></html>");
        return sb.ToString();
    }

    private async void ShowDialog(string title, string msg)
    {
        var d = new ContentDialog
        {
            Title = title,
            Content = msg,
            PrimaryButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await d.ShowAsync();
    }

    private sealed record BoostHistoryRow(BoostHistoryEntry Entry)
    {
        public string AppliedAt => Entry.AppliedAt.ToString("dd/MM/yy HH:mm");
        public string HealthScoreBefore => $"Avant: {Entry.HealthScoreBefore}";
        public string HealthScoreAfter => $"Apres: {Entry.HealthScoreAfter}";
        public string ActionsLabel => string.Join(", ", Entry.Actions.Take(3));
    }
}
