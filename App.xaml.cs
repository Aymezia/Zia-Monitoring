using Microsoft.Extensions.DependencyInjection;
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
    private readonly ServiceProvider _services;

    /// <summary>Fenêtre principale (nécessaire aux FileOpen/SavePicker en app non packagée).</summary>
    public Window? MainWindowInstance => _window;

    /// <summary>Conteneur d'injection de dépendances de l'application.</summary>
    public IServiceProvider Services => _services;

    public AppStateViewModel State => _services.GetRequiredService<AppStateViewModel>();
    public Application.MonitoringService MonitoringService => _services.GetRequiredService<Application.MonitoringService>();
    public Application.BoostEngine BoostEngine => _services.GetRequiredService<Application.BoostEngine>();
    public Application.SupportExportService SupportExportService => _services.GetRequiredService<Application.SupportExportService>();
    public Application.GamerTroubleshootingService GamerTroubleshootingService => _services.GetRequiredService<Application.GamerTroubleshootingService>();
    public Application.ActiveGameDetector ActiveGameDetector => _services.GetRequiredService<Application.ActiveGameDetector>();
    public Application.BoostHistoryService BoostHistoryService => _services.GetRequiredService<Application.BoostHistoryService>();
    public Application.SettingsService SettingsService => _services.GetRequiredService<Application.SettingsService>();
    public Application.AlertNotificationService AlertNotificationService => _services.GetRequiredService<Application.AlertNotificationService>();
    public Application.SilentModeService SilentModeService => _services.GetRequiredService<Application.SilentModeService>();
    public Application.OptimizationProfileService OptimizationProfileService => _services.GetRequiredService<Application.OptimizationProfileService>();
    public Application.BrowserCacheCleanerService BrowserCacheCleaner => _services.GetRequiredService<Application.BrowserCacheCleanerService>();
    public Application.SecurityScanService SecurityScanner => _services.GetRequiredService<Application.SecurityScanService>();
    public Application.SecurityReportExportService SecurityReportExporter => _services.GetRequiredService<Application.SecurityReportExportService>();
    public Application.MetricsHistoryService MetricsHistory => _services.GetRequiredService<Application.MetricsHistoryService>();
    public Application.UpdateCheckService UpdateChecker => _services.GetRequiredService<Application.UpdateCheckService>();
    public Application.GameSessionService GameSessions => _services.GetRequiredService<Application.GameSessionService>();
    public Application.GameBoosterService GameBooster => _services.GetRequiredService<Application.GameBoosterService>();
    public Application.PresentMonService PresentMon => _services.GetRequiredService<Application.PresentMonService>();
    public Application.DeepCleanService DeepClean => _services.GetRequiredService<Application.DeepCleanService>();
    public Application.ProcessRuleService ProcessRules => _services.GetRequiredService<Application.ProcessRuleService>();
    public Application.PrivacyScanService PrivacyScanner => _services.GetRequiredService<Application.PrivacyScanService>();
    public Application.HibpService Hibp => _services.GetRequiredService<Application.HibpService>();
    public Application.GeoIpService GeoIp => _services.GetRequiredService<Application.GeoIpService>();
    public Application.SmartTrendService SmartTrend => _services.GetRequiredService<Application.SmartTrendService>();
    public Application.StartupManagerService StartupManager => _services.GetRequiredService<Application.StartupManagerService>();
    public Application.DebloatService Debloat => _services.GetRequiredService<Application.DebloatService>();
    public Application.RegionalLatencyService RegionalLatency => _services.GetRequiredService<Application.RegionalLatencyService>();
    public Application.GraphicsPresetService GraphicsPreset => _services.GetRequiredService<Application.GraphicsPresetService>();
    public Application.DeviceAccessAuditService DeviceAccessAudit => _services.GetRequiredService<Application.DeviceAccessAuditService>();
    public Application.RestorePointService RestorePoint => _services.GetRequiredService<Application.RestorePointService>();
    public Application.GameSaveBackupService SaveBackup => _services.GetRequiredService<Application.GameSaveBackupService>();
    public Application.CustomRuleEngine CustomRules => _services.GetRequiredService<Application.CustomRuleEngine>();
    public Application.PrometheusExporterService PrometheusExporter => _services.GetRequiredService<Application.PrometheusExporterService>();
    public Application.ObsWebSocketService ObsWebSocket => _services.GetRequiredService<Application.ObsWebSocketService>();
    public Application.DnsService Dns => _services.GetRequiredService<Application.DnsService>();
    public Application.SystemHealthService SystemHealth => _services.GetRequiredService<Application.SystemHealthService>();
    public Application.GameLaunchProfileService GameLaunchProfiles => _services.GetRequiredService<Application.GameLaunchProfileService>();
    public Application.AchievementService Achievements => _services.GetRequiredService<Application.AchievementService>();
    public Application.CrashDiagnosticsService CrashDiagnostics => _services.GetRequiredService<Application.CrashDiagnosticsService>();
    public Application.BatteryService Battery => _services.GetRequiredService<Application.BatteryService>();
    public Application.WindowsUpdateHistoryService WindowsUpdates => _services.GetRequiredService<Application.WindowsUpdateHistoryService>();
    public Application.FirewallKillSwitchService KillSwitch => _services.GetRequiredService<Application.FirewallKillSwitchService>();
    public Application.BrowserExtensionAuditService BrowserExtensions => _services.GetRequiredService<Application.BrowserExtensionAuditService>();
    public Application.AppWatchdogService Watchdog => _services.GetRequiredService<Application.AppWatchdogService>();
    public Application.SmartRebootService SmartReboot => _services.GetRequiredService<Application.SmartRebootService>();
    public Application.DiscordRichPresenceService DiscordPresence => _services.GetRequiredService<Application.DiscordRichPresenceService>();
    public Application.PublicIpMonitorService PublicIpMonitor => _services.GetRequiredService<Application.PublicIpMonitorService>();
    public Application.WakeOnLanService WakeOnLan => _services.GetRequiredService<Application.WakeOnLanService>();
    public Application.SelfUpdateService SelfUpdater => _services.GetRequiredService<Application.SelfUpdateService>();
    public Application.PcAuditService PcAudit => _services.GetRequiredService<Application.PcAuditService>();

    /// <summary>
    /// Action de débloat interrompue par une relance élevée (voir
    /// AdminElevationPrompt), à terminer automatiquement au démarrage de
    /// cette instance — sinon l'utilisateur devrait recliquer manuellement
    /// une fois l'app relancée, ce qui donnait l'impression que le débloat
    /// "ne marchait pas".
    /// </summary>
    public Application.DebloatResumeAction? PendingDebloatResume { get; private set; }

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        _services = ConfigureServices();

        PendingDebloatResume = Application.DebloatService.TryParseResumeArgs(Environment.GetCommandLineArgs());

        // Doit être appliqué avant InitializeComponent() : WinUI resout les
        // ressources x:Uid a la lecture du XAML, pas dynamiquement ensuite.
        try
        {
            var language = _services.GetRequiredService<Application.SettingsService>().Load().Language;
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        }
        catch { }

        this.UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        Log("Application constructor initialized.");
        InitializeComponent();

        try { AppNotificationManager.Default.Register(); } catch { }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<AppStateViewModel>();
        services.AddSingleton<Application.MonitoringService>();
        services.AddSingleton<Application.BoostEngine>();
        services.AddSingleton<Application.SupportExportService>();
        services.AddSingleton<Application.GamerTroubleshootingService>();
        services.AddSingleton<Application.ActiveGameDetector>();
        services.AddSingleton<Application.BoostHistoryService>();
        services.AddSingleton<Application.SettingsService>();
        services.AddSingleton<Application.AlertNotificationService>();
        services.AddSingleton<Application.SilentModeService>();
        services.AddSingleton<Application.OptimizationProfileService>();
        services.AddSingleton<Application.BrowserCacheCleanerService>();
        services.AddSingleton<Application.SecurityScanService>();
        services.AddSingleton<Application.SecurityReportExportService>();
        services.AddSingleton<Application.MetricsHistoryService>();
        services.AddSingleton<Application.UpdateCheckService>();
        services.AddSingleton<Application.GameSessionService>();
        services.AddSingleton<Application.GameBoosterService>();
        services.AddSingleton<Application.PresentMonService>();
        services.AddSingleton<Application.DeepCleanService>();
        services.AddSingleton<Application.ProcessRuleService>();
        services.AddSingleton<Application.PrivacyScanService>();
        services.AddSingleton<Application.HibpService>();
        services.AddSingleton<Application.GeoIpService>();
        services.AddSingleton<Application.SmartTrendService>();
        services.AddSingleton<Application.StartupManagerService>();
        services.AddSingleton<Application.DebloatService>();
        services.AddSingleton<Application.RegionalLatencyService>();
        services.AddSingleton<Application.GraphicsPresetService>();
        services.AddSingleton<Application.DeviceAccessAuditService>();
        services.AddSingleton<Application.RestorePointService>();
        services.AddSingleton<Application.GameSaveBackupService>();
        services.AddSingleton<Application.CustomRuleEngine>();
        services.AddSingleton<Application.PrometheusExporterService>();
        services.AddSingleton<Application.ObsWebSocketService>();
        services.AddSingleton<Application.DnsService>();
        services.AddSingleton<Application.SystemHealthService>();
        services.AddSingleton<Application.GameLaunchProfileService>();
        services.AddSingleton<Application.AchievementService>();
        services.AddSingleton<Application.CrashDiagnosticsService>();
        services.AddSingleton<Application.BatteryService>();
        services.AddSingleton<Application.WindowsUpdateHistoryService>();
        services.AddSingleton<Application.FirewallKillSwitchService>();
        services.AddSingleton<Application.BrowserExtensionAuditService>();
        services.AddSingleton<Application.AppWatchdogService>();
        services.AddSingleton<Application.SmartRebootService>();
        services.AddSingleton<Application.DiscordRichPresenceService>();
        services.AddSingleton<Application.PublicIpMonitorService>();
        services.AddSingleton<Application.WakeOnLanService>();
        services.AddSingleton<Application.SelfUpdateService>();
        services.AddSingleton<Application.PcAuditService>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Libère les services (dont LibreHardwareMonitor, qui doit fermer son
    /// pilote noyau). Appelé à la fermeture de la fenêtre principale.
    /// </summary>
    public void ShutdownServices()
    {
        try
        {
            _services.Dispose();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Libération des services incomplète", ex);
        }
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
        Infrastructure.AppLog.Error("Exception WinUI non geree", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        Infrastructure.AppLog.Error($"Exception AppDomain non geree: {e.ExceptionObject}");
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Infrastructure.AppLog.Error("Exception de tache non observee", e.Exception);
        e.SetObserved();
    }

    private static void Log(string message) => Infrastructure.AppLog.Info(message);
}
