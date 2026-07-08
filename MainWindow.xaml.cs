using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure;
using ZiaMonitoring_App.Pages;

namespace ZiaMonitoring_App;

public sealed partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x515A;
    private const uint ModShift = 0x0004;
    private const uint ModControl = 0x0002;
    private const uint VkZ = 0x5A;
    private const int GwlpWndProc = -4;
    private const int SwHide = 0;
    private const int SwShow = 5;

    private readonly CancellationTokenSource _monitoringCts = new();
    private Task? _monitoringLoop;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
    private readonly nint _hwnd;
    private readonly WindowProc _windowProc;
    private nint _oldWindowProc;
    private SystrayIcon? _systray;
    private PerformanceOverlayWindow? _overlayWindow;
    private bool _hotkeyRegistered;
    private bool _mainWindowVisible = true;
    private bool _obsGameSceneActive;
    private string? _lastClipboardClearGame;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _hwnd = WindowNative.GetWindowHandle(this);
        _dispatcherQueue = DispatcherQueue;
        _windowProc = WndProc;
        _oldWindowProc = SetWindowLongPtr(_hwnd, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_windowProc));
        Closed += MainWindow_Closed;

        NavFrame.Navigate(typeof(DashboardPage));

        var app = (App)Microsoft.UI.Xaml.Application.Current;
        ConfigureHotkey(app.SettingsService.Load());
        app.Achievements.Increment("app_launches");

        // La collecte (WMI, LibreHardwareMonitor, pings) tourne hors du thread UI ;
        // seul le report des résultats repasse par le DispatcherQueue.
        _monitoringLoop = Task.Run(() => MonitoringLoopAsync(_monitoringCts.Token));

        _ = Task.Run(NotifyIfUpdateAvailableAsync);
    }

    private static async Task NotifyIfUpdateAvailableAsync()
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var update = await app.UpdateChecker.CheckForUpdateAsync().ConfigureAwait(false);
        if (update is not null)
        {
            ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                "Zia Monitoring - Mise a jour disponible",
                $"La version {update.TagName} est disponible. Ouvrez A propos pour telecharger.");
        }
    }

    private async Task MonitoringLoopAsync(CancellationToken ct)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var lastHistoryPush = DateTime.MinValue;
        var lastThermalDriftCheck = DateTime.MinValue;
        var lastBsodCheck = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            var cycleStart = Environment.TickCount64;
            var settings = app.SettingsService.Load();

            try
            {
                var frame = app.MonitoringService.CaptureFrame();
                var activeGame = app.ActiveGameDetector.DetectActiveGame(
                    frame.Snapshot.ActiveTcpConnections,
                    frame.Snapshot.EstimatedFps);

                app.MetricsHistory.Record(frame.Snapshot);
                app.PrometheusExporter.UpdateFrame(frame);

                if (settings.EnablePrometheusExporter && !app.PrometheusExporter.IsRunning)
                    app.PrometheusExporter.Start();
                else if (!settings.EnablePrometheusExporter && app.PrometheusExporter.IsRunning)
                    app.PrometheusExporter.Stop();
                app.PresentMon.EnsureTracking(activeGame?.Pid);
                app.ProcessRules.ApplyRules();

                var finishedSession = app.GameSessions.OnMonitoringTick(activeGame, frame.Snapshot);
                if (finishedSession is not null)
                {
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        $"Session {finishedSession.GameName} terminée",
                        $"{finishedSession.DurationLabel} · {finishedSession.FpsLabel} · {finishedSession.TempLabel}");

                    if (settings.EnableNtfyNotifications)
                    {
                        _ = ZiaMonitoring_App.Application.NtfyNotificationService.SendAsync(
                            settings.NtfyTopic, $"Session {finishedSession.GameName} terminée",
                            $"{finishedSession.DurationLabel} · {finishedSession.FpsLabel} · {finishedSession.TempLabel}");
                    }
                }

                if (app.GameSessions.ShouldNotifyWeeklyGoalExceeded(settings.WeeklyPlaytimeGoalHours))
                {
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Objectif de temps de jeu atteint",
                        $"Vous avez dépassé {settings.WeeklyPlaytimeGoalHours:F0} h de jeu cette semaine.");
                }

                // Les graphes 24 h / 7 j sont réalimentés depuis SQLite toutes
                // les minutes (et au premier cycle après démarrage).
                IReadOnlyList<double>? hourlyCpu = null;
                IReadOnlyList<double>? dailyCpu = null;
                if (DateTime.UtcNow - lastHistoryPush >= TimeSpan.FromMinutes(1))
                {
                    lastHistoryPush = DateTime.UtcNow;
                    hourlyCpu = app.MetricsHistory.GetCpuHourlyAverages24h();
                    dailyCpu = app.MetricsHistory.GetCpuDailyAverages7d();
                }

                if (settings.AutoSilentModeOnGame)
                {
                    var warnings = new List<string>();
                    if (activeGame is not null && !app.SilentModeService.IsActive)
                        app.SilentModeService.Activate(warnings);
                    else if (activeGame is null && app.SilentModeService.IsActive)
                        app.SilentModeService.Deactivate(warnings);

                    foreach (var warning in warnings)
                        AppLog.Warn($"Mode silencieux: {warning}");
                }

                if (settings.EnableGameBooster)
                {
                    if (activeGame is not null && !app.GameBooster.IsActive)
                    {
                        var actions = app.GameBooster.Activate(activeGame.Pid, activeGame.GameName);
                        if (actions.Count > 0)
                        {
                            ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                                $"Game Booster activé — {activeGame.GameName}",
                                string.Join(" · ", actions));
                        }
                    }
                    else if (activeGame is null && app.GameBooster.IsActive)
                    {
                        app.GameBooster.Deactivate();
                    }
                }
                else if (app.GameBooster.IsActive)
                {
                    app.GameBooster.Deactivate();
                }

                if (settings.EnableGameLaunchProfiles)
                {
                    if (activeGame is not null && !app.GameLaunchProfiles.IsActive)
                    {
                        var actions = app.GameLaunchProfiles.Activate(activeGame.GameName);
                        if (actions.Count > 0)
                        {
                            ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                                $"Profil de lancement — {activeGame.GameName}",
                                string.Join(" · ", actions));
                        }
                    }
                    else if (activeGame is null && app.GameLaunchProfiles.IsActive)
                    {
                        app.GameLaunchProfiles.Deactivate();
                    }
                }
                else if (app.GameLaunchProfiles.IsActive)
                {
                    app.GameLaunchProfiles.Deactivate();
                }

                if (settings.EnableClipboardClearOnGameLaunch && activeGame is not null && activeGame.GameName != _lastClipboardClearGame)
                {
                    _lastClipboardClearGame = activeGame.GameName;
                    try { Windows.ApplicationModel.DataTransfer.Clipboard.Clear(); }
                    catch (Exception ex) { AppLog.Warn("Vidage du presse-papiers au lancement du jeu impossible", ex); }
                }
                else if (activeGame is null)
                {
                    _lastClipboardClearGame = null;
                }

                // Plan d'alimentation selon la source (laptop uniquement — no-op sans batterie).
                if (settings.EnableAutoPowerProfile)
                {
                    if (app.Battery.ApplyPowerSourceProfile() is { } powerMessage)
                        ZiaMonitoring_App.Application.AlertNotificationService.SendToast("Zia Monitoring - Alimentation", powerMessage);
                }
                else
                {
                    app.Battery.ResetPowerSourceTracking();
                }

                // Watchdog : relance des apps surveillées disparues.
                if (settings.EnableAppWatchdog)
                {
                    foreach (var restartedApp in app.Watchdog.Tick())
                    {
                        ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                            "Zia Monitoring - Watchdog", $"{restartedApp} s'est arrêté de façon inattendue et a été relancé.");
                    }
                }

                // Proposition de redémarrage si l'uptime dépasse le seuil (hors jeu).
                if (settings.EnableSmartReboot
                    && app.SmartReboot.Tick(frame.Profile.Uptime, activeGame is not null, settings.SmartRebootUptimeDays) is { } rebootMessage)
                {
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Zia Monitoring - Redémarrage conseillé", rebootMessage);
                }

                // Discord Rich Presence (throttlé à 15 s côté service).
                if (settings.EnableDiscordRichPresence && !string.IsNullOrWhiteSpace(settings.DiscordApplicationId))
                {
                    var presenceDetails = activeGame is not null ? $"Joue à {activeGame.GameName}" : "Surveille son PC";
                    var presenceState = $"CPU {frame.Snapshot.CpuPercent:F0}% · {frame.Snapshot.EstimatedFps:F0} FPS";
                    var sessionStart = activeGame is not null ? DateTime.UtcNow - activeGame.SessionDuration : (DateTime?)null;
                    app.DiscordPresence.UpdatePresence(settings.DiscordApplicationId, presenceDetails, presenceState, sessionStart);
                }
                else
                {
                    app.DiscordPresence.ClearPresence();
                }

                if (settings.EnableObsAutoSceneSwitch)
                {
                    var shouldShowGameScene = activeGame is not null;
                    if (shouldShowGameScene != _obsGameSceneActive)
                    {
                        _obsGameSceneActive = shouldShowGameScene;
                        var targetScene = shouldShowGameScene ? settings.ObsGameSceneName : settings.ObsIdleSceneName;

                        if (!string.IsNullOrWhiteSpace(targetScene))
                        {
                            if (!app.ObsWebSocket.IsConnected)
                                await app.ObsWebSocket.ConnectAsync(settings.ObsHost, settings.ObsPort, settings.ObsPassword, ct);

                            var (success, message) = await app.ObsWebSocket.SetSceneAsync(targetScene, ct);
                            if (!success)
                                AppLog.Warn($"Bascule de scène OBS automatique en échec : {message}");
                        }
                    }
                }

                app.AlertNotificationService.CheckAndNotify(frame, settings);
                app.AlertNotificationService.CheckWifiDuringGame(activeGame, frame.ConnectionKind, settings);

                if (frame.ThrottlingToast is not null)
                {
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Zia Monitoring - Throttling détecté", frame.ThrottlingToast);
                }

                foreach (var rule in app.CustomRules.Evaluate(frame.Snapshot, frame.Profile))
                {
                    switch (rule.Action)
                    {
                        case ZiaMonitoring_App.Application.RuleAction.RunDeepClean:
                            app.DeepClean.Run();
                            break;
                        case ZiaMonitoring_App.Application.RuleAction.OpenTaskManager:
                            try { System.Diagnostics.Process.Start("taskmgr.exe"); } catch { }
                            break;
                    }

                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Zia Monitoring - Règle personnalisée", rule.Description);
                }

                // Snapshot + tendance SMART une fois par jour.
                if (app.SmartTrend.IsDailyCheckDue())
                {
                    foreach (var smartWarning in app.SmartTrend.RecordAndAnalyze())
                    {
                        ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                            "Zia Monitoring - Disque en dégradation", smartWarning.Label);
                    }
                }

                // Dérive thermique long terme, une fois par jour.
                if (DateTime.UtcNow - lastThermalDriftCheck >= TimeSpan.FromHours(24))
                {
                    lastThermalDriftCheck = DateTime.UtcNow;
                    foreach (var driftWarning in app.MetricsHistory.GetThermalDriftWarnings())
                    {
                        ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                            "Zia Monitoring - Dérive thermique", driftWarning);
                    }
                }

                // Nouveau BSOD depuis le dernier redémarrage, vérifié toutes les 30 min.
                if (DateTime.UtcNow - lastBsodCheck >= TimeSpan.FromMinutes(30))
                {
                    lastBsodCheck = DateTime.UtcNow;
                    if (app.CrashDiagnostics.CheckForNewBsodSinceLastNotified() is { } newBsod)
                    {
                        ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                            "Zia Monitoring - Écran bleu détecté", newBsod.Label);

                        if (settings.EnableNtfyNotifications)
                        {
                            _ = ZiaMonitoring_App.Application.NtfyNotificationService.SendAsync(
                                settings.NtfyTopic, "Zia Monitoring - Écran bleu détecté", newBsod.Label);
                        }
                    }
                }

                // Alerte de changement d'IP publique (le service se limite lui-même à 15 min).
                if (settings.EnablePublicIpAlert
                    && await app.PublicIpMonitor.CheckForChangeAsync(ct) is { } newPublicIp)
                {
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Zia Monitoring - IP publique modifiée", $"Nouvelle adresse IP publique : {newPublicIp}");

                    if (settings.EnableNtfyNotifications)
                    {
                        _ = ZiaMonitoring_App.Application.NtfyNotificationService.SendAsync(
                            settings.NtfyTopic, "Zia Monitoring - IP publique modifiée", $"Nouvelle adresse IP publique : {newPublicIp}");
                    }
                }

                // Nettoyage planifié quotidien (le jeu actif n'est jamais interrompu).
                if (activeGame is null
                    && app.DeepClean.IsScheduledRunDue(settings.EnableCleanupScheduler, settings.ScheduledCleanupTime))
                {
                    var cleanResults = app.DeepClean.Run();
                    var cleaned = cleanResults.Where(r => r.Error is null).ToList();
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Nettoyage planifié terminé",
                        $"{cleaned.Sum(r => r.DeletedFiles)} fichier(s) supprimé(s), {cleaned.Sum(r => r.FreedMb):F0} Mo libérés.");
                }

                // Sauvegarde planifiée des saves de jeux (même heure que le nettoyage).
                if (activeGame is null
                    && app.SaveBackup.IsScheduledRunDue(settings.EnableScheduledSaveBackup, settings.ScheduledCleanupTime))
                {
                    var backupResult = app.SaveBackup.BackupNow();
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        "Sauvegarde des saves de jeux terminée", backupResult.Summary);
                }

                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        app.State.Update(frame);
                        app.State.UpdateActiveGame(activeGame);
                        app.Achievements.SetCounter("games_detected", frame.Profile.InstalledGames.Count);
                        if (hourlyCpu is not null && dailyCpu is not null)
                            app.State.UpdatePersistedCpuHistory(hourlyCpu, dailyCpu);
                        UpdateTitleBar(frame, settings);
                        ConfigureSystray(settings, frame);
                        ConfigureOverlay(settings, activeGame);
                        ConfigureHotkey(settings);
                    }
                    catch (Exception ex)
                    {
                        // La fenêtre peut être en cours de fermeture.
                        AppLog.Warn("Mise à jour UI du monitoring ignorée", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error("Cycle de monitoring en échec", ex);
            }

            var interval = TimeSpan.FromSeconds(Math.Clamp(settings.RefreshIntervalSeconds, 1, 10));
            var elapsed = TimeSpan.FromMilliseconds(Environment.TickCount64 - cycleStart);
            var delay = interval - elapsed;

            try
            {
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(50), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void UpdateTitleBar(MonitoringFrame frame, AppSettings settings)
    {
        var cpuT = frame.Snapshot.CpuTemperatureC is { } ct ? $"{ct:F0}C" : "N/A";
        var gpuT = frame.Snapshot.GpuTemperatureC is { } gt ? $"{gt:F0}C" : "N/A";
        var tempAlert = frame.Snapshot.CpuTemperatureC >= settings.CpuTempAlertThresholdC
                        || frame.Snapshot.GpuTemperatureC >= settings.GpuTempAlertThresholdC;
        var fps = frame.Snapshot.EstimatedFps > 0 ? $" | FPS {frame.Snapshot.EstimatedFps:F0}" : string.Empty;

        AppTitleBar.Title = tempAlert ? "Zia Monitoring - ALERTE TEMP" : "Zia Monitoring";
        AppTitleBar.Subtitle = tempAlert
            ? $"Temperature persistante  CPU {cpuT}  |  GPU {gpuT}{fps}"
            : $"CPU {frame.Snapshot.CpuPercent:F0}%  {cpuT}  |  GPU {frame.Snapshot.GpuUsagePercent ?? 0:F0}%  {gpuT}{fps}";
    }

    private void ConfigureSystray(AppSettings settings, MonitoringFrame frame)
    {
        if (!settings.ShowSystray)
        {
            _systray?.Dispose();
            _systray = null;
            return;
        }

        _systray ??= new SystrayIcon(_hwnd);
        var cpuT = frame.Snapshot.CpuTemperatureC is { } ct ? $"{ct:F0}C" : "N/A";
        var gpuT = frame.Snapshot.GpuTemperatureC is { } gt ? $"{gt:F0}C" : "N/A";
        var fps = frame.Snapshot.EstimatedFps > 0 ? $" | FPS {frame.Snapshot.EstimatedFps:F0}" : string.Empty;
        _systray.UpdateTooltip($"Zia | CPU {frame.Snapshot.CpuPercent:F0}% {cpuT} | GPU {frame.Snapshot.GpuUsagePercent ?? 0:F0}% {gpuT}{fps}");
    }

    private void ConfigureOverlay(AppSettings settings, ActiveGameSession? activeGame)
    {
        var shouldShow = settings.EnableMiniWidget || (settings.EnableGameOverlay && activeGame is not null);
        if (!shouldShow)
        {
            _overlayWindow?.HideOverlay();
            return;
        }

        if (_overlayWindow is null)
        {
            _overlayWindow = new PerformanceOverlayWindow();
            _overlayWindow.Activate();
            _overlayWindow.HideOverlay();
        }

        _overlayWindow.ApplySettings(settings);
        _overlayWindow.ShowOverlay();
    }

    private void ConfigureHotkey(AppSettings settings)
    {
        if (settings.EnableGlobalHotkey && !_hotkeyRegistered)
        {
            _hotkeyRegistered = RegisterHotKey(_hwnd, HotkeyId, ModControl | ModShift, VkZ);
        }
        else if (!settings.EnableGlobalHotkey && _hotkeyRegistered)
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyRegistered = false;
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void ToggleMainWindowVisibility()
    {
        _mainWindowVisible = !_mainWindowVisible;
        ShowWindow(_hwnd, _mainWindowVisible ? SwShow : SwHide);
        if (_mainWindowVisible)
            Activate();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "dashboard":
                    NavFrame.Navigate(typeof(DashboardPage));
                    break;
                case "mapping":
                    NavFrame.Navigate(typeof(MappingPage));
                    break;
                case "health":
                    NavFrame.Navigate(typeof(HealthPage));
                    break;
                case "recommendations":
                    NavFrame.Navigate(typeof(RecommendationsPage));
                    break;
                case "boost":
                    NavFrame.Navigate(typeof(BoostPage));
                    break;
                case "assistance":
                    NavFrame.Navigate(typeof(AssistancePage));
                    break;
                case "gamer-support":
                    NavFrame.Navigate(typeof(GamerSupportPage));
                    break;
                case "detailed-logs":
                    NavFrame.Navigate(typeof(DetailedLogsPage));
                    break;
                case "profiles":
                    NavFrame.Navigate(typeof(ProfilesPage));
                    break;
                case "security":
                    NavFrame.Navigate(typeof(SecurityPage));
                    break;
                case "network":
                    NavFrame.Navigate(typeof(NetworkPage));
                    break;
                case "maintenance":
                    NavFrame.Navigate(typeof(MaintenancePage));
                    break;
            }
        }
    }

    private sealed record PaletteCommand(string Category, string Title, Action Execute);

    private List<PaletteCommand>? _paletteCommands;

    private List<PaletteCommand> BuildPaletteCommands()
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;

        (string, string, Type)[] pages =
        [
            ("Navigation", "Dashboard", typeof(DashboardPage)),
            ("Navigation", "Mapping PC", typeof(MappingPage)),
            ("Navigation", "Santé", typeof(HealthPage)),
            ("Navigation", "Recommandations", typeof(RecommendationsPage)),
            ("Navigation", "Boost", typeof(BoostPage)),
            ("Navigation", "Assistance", typeof(AssistancePage)),
            ("Navigation", "Gamer / Streamer", typeof(GamerSupportPage)),
            ("Navigation", "Logs détaillés", typeof(DetailedLogsPage)),
            ("Navigation", "Profils", typeof(ProfilesPage)),
            ("Navigation", "Sécurité", typeof(SecurityPage)),
            ("Navigation", "Réseau", typeof(NetworkPage)),
            ("Navigation", "Maintenance", typeof(MaintenancePage)),
            ("Navigation", "Paramètres", typeof(SettingsPage))
        ];

        var commands = pages
            .Select(p => new PaletteCommand(p.Item1, p.Item2, () => NavFrame.Navigate(p.Item3)))
            .ToList();

        commands.Add(new PaletteCommand("Action", "Basculer le mode silencieux", () =>
        {
            var settings = app.SettingsService.Load();
            app.SettingsService.Save(settings with { AutoSilentModeOnGame = !settings.AutoSilentModeOnGame });
        }));
        commands.Add(new PaletteCommand("Action", "Basculer le Game Booster automatique", () =>
        {
            var settings = app.SettingsService.Load();
            app.SettingsService.Save(settings with { EnableGameBooster = !settings.EnableGameBooster });
        }));
        commands.Add(new PaletteCommand("Action", "Ouvrir le Gestionnaire des tâches", () =>
        {
            try { System.Diagnostics.Process.Start("taskmgr.exe"); } catch { }
        }));
        commands.Add(new PaletteCommand("Action", "Quitter Zia Monitoring", () => Microsoft.UI.Xaml.Application.Current.Exit()));

        return commands;
    }

    private void CommandPaletteAccelerator_Invoked(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (CommandPaletteOverlay.Visibility == Visibility.Visible)
            CloseCommandPalette();
        else
            OpenCommandPalette();
    }

    private void OpenCommandPalette()
    {
        _paletteCommands ??= BuildPaletteCommands();
        CommandPaletteBox.Text = string.Empty;
        FilterPaletteCommands(string.Empty);
        CommandPaletteOverlay.Visibility = Visibility.Visible;
        CommandPaletteBox.Focus(FocusState.Programmatic);
    }

    private void CloseCommandPalette() => CommandPaletteOverlay.Visibility = Visibility.Collapsed;

    private void FilterPaletteCommands(string query)
    {
        var commands = _paletteCommands ?? [];
        CommandPaletteList.ItemsSource = string.IsNullOrWhiteSpace(query)
            ? commands
            : commands.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void CommandPaletteBox_TextChanged(object sender, TextChangedEventArgs e) => FilterPaletteCommands(CommandPaletteBox.Text);

    private void CommandPaletteBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseCommandPalette();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Enter)
        {
            var first = (CommandPaletteList.ItemsSource as IEnumerable<PaletteCommand>)?.FirstOrDefault();
            if (first is not null)
            {
                first.Execute();
                CloseCommandPalette();
            }
            e.Handled = true;
        }
    }

    private void CommandPaletteList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PaletteCommand command)
        {
            command.Execute();
            CloseCommandPalette();
        }
    }

    private void CommandPaletteOverlay_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => CloseCommandPalette();

    private void CommandPalettePanel_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => e.Handled = true;

    private nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (message == WmHotkey && wParam == HotkeyId)
        {
            ToggleMainWindowVisibility();
            return 0;
        }

        return CallWindowProc(_oldWindowProc, hwnd, message, wParam, lParam);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _monitoringCts.Cancel();

        // Laisse le cycle de collecte en cours se terminer avant de libérer
        // les services (LibreHardwareMonitor ne doit pas être fermé en pleine
        // lecture). Le lambda UI en attente n'est pas bloquant : TryEnqueue
        // est fire-and-forget côté boucle.
        try { _monitoringLoop?.Wait(TimeSpan.FromSeconds(3)); } catch { }

        if (_hotkeyRegistered)
            UnregisterHotKey(_hwnd, HotkeyId);

        _systray?.Dispose();
        _overlayWindow?.Close();

        if (_oldWindowProc != nint.Zero)
            SetWindowLongPtr(_hwnd, GwlpWndProc, _oldWindowProc);

        ((App)Microsoft.UI.Xaml.Application.Current).ShutdownServices();
    }

    private delegate nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int dwNewLong);

    private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32());
    }
}
