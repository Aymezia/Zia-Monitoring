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

                var finishedSession = app.GameSessions.OnMonitoringTick(activeGame, frame.Snapshot);
                if (finishedSession is not null)
                {
                    ZiaMonitoring_App.Application.AlertNotificationService.SendToast(
                        $"Session {finishedSession.GameName} terminée",
                        $"{finishedSession.DurationLabel} · {finishedSession.FpsLabel} · {finishedSession.TempLabel}");
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

                app.AlertNotificationService.CheckAndNotify(frame, settings);

                _dispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        app.State.Update(frame);
                        app.State.UpdateActiveGame(activeGame);
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
            }
        }
    }

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
