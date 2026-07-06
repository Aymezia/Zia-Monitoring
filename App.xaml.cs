using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;
using ZiaMonitoring_App.ViewModels;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ZiaMonitoring_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _window;
    private readonly string _logFile;

    public AppStateViewModel State { get; } = new();

    public ZiaMonitoring_App.Application.MonitoringService MonitoringService { get; } = new();
    public ZiaMonitoring_App.Application.BoostEngine BoostEngine { get; } = new();
    public ZiaMonitoring_App.Application.SupportExportService SupportExportService { get; } = new();
    public ZiaMonitoring_App.Application.GamerTroubleshootingService GamerTroubleshootingService { get; } = new();
    public ZiaMonitoring_App.Application.ActiveGameDetector ActiveGameDetector { get; } = new();
    public ZiaMonitoring_App.Application.BoostHistoryService BoostHistoryService { get; } = new();
    public ZiaMonitoring_App.Application.SettingsService SettingsService { get; } = new();
    public ZiaMonitoring_App.Application.AlertNotificationService AlertNotificationService { get; } = new();
    public ZiaMonitoring_App.Application.SilentModeService SilentModeService { get; } = new();
    public ZiaMonitoring_App.Application.OptimizationProfileService OptimizationProfileService { get; } = new();
    public ZiaMonitoring_App.Application.BrowserCacheCleanerService BrowserCacheCleaner { get; } = new();
    public ZiaMonitoring_App.Application.SecurityScanService SecurityScanner { get; } = new();
    
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZiaMonitoring", "logs");
        Directory.CreateDirectory(logDir);
        _logFile = Path.Combine(logDir, "startup.log");

        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        Log("Application constructor initialized.");
        InitializeComponent();

        try { AppNotificationManager.Default.Register(); } catch { }
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched started.");
            _window = new MainWindow();
            _window.Activate();
            Log("MainWindow activated.");
        }
        catch (Exception ex)
        {
            Log($"Launch failure: {ex}");
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log($"WinUI unhandled exception: {e.Exception}");
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        Log($"AppDomain unhandled exception: {e.ExceptionObject}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log($"TaskScheduler unobserved exception: {e.Exception}");
        e.SetObserved();
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging should never crash the app.
        }
    }
}
