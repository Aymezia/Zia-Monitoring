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
            GameBoosterToggle.IsOn = _settings.EnableGameBooster;
            SilentModeToggle.IsOn = _settings.AutoSilentModeOnGame;
            SchedulerToggle.IsOn = _settings.EnableCleanupScheduler;

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

    private void MiniOpacitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (MiniOpacityValueLabel != null)
            MiniOpacityValueLabel.Text = $"{e.NewValue:F0}%";

        _settings = _settings with { MiniWidgetOpacity = Math.Clamp(e.NewValue / 100.0, 0.35, 1.0) };
        SaveSettings();
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

    private void Scheduler_Toggled(object sender, RoutedEventArgs e)
    {
        _settings = _settings with { EnableCleanupScheduler = SchedulerToggle.IsOn };
        SaveSettings();
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
