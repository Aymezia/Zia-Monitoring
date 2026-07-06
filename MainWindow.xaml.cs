using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Pages;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ZiaMonitoring_App;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += Timer_Tick;
        _timer.Start();

        NavFrame.Navigate(typeof(DashboardPage));
    }

    private void Timer_Tick(object? sender, object e)
    {
        var app = (App)Microsoft.UI.Xaml.Application.Current;
        var settings = app.SettingsService.Load();

        // Adjust timer interval if setting changed
        if (_timer.Interval.TotalSeconds != settings.RefreshIntervalSeconds)
            _timer.Interval = TimeSpan.FromSeconds(Math.Clamp(settings.RefreshIntervalSeconds, 1, 10));

        var frame = app.MonitoringService.CaptureFrame();
        app.State.Update(frame);

        // Active game detection + silent mode
        var activeGame = app.ActiveGameDetector.DetectActiveGame();
        app.State.UpdateActiveGame(activeGame);
        if (settings.AutoSilentModeOnGame)
        {
            var warnings = new List<string>();
            if (activeGame is not null && !app.SilentModeService.IsActive)
                app.SilentModeService.Activate(warnings);
            else if (activeGame is null && app.SilentModeService.IsActive)
                app.SilentModeService.Deactivate(warnings);
        }

        // Toast notifications
        app.AlertNotificationService.CheckAndNotify(frame, settings);
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
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
                default:
                    break;
            }
        }
    }
}
