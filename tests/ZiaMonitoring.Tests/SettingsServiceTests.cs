using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));

    private static AppSettings BaseSettings => new(
        RefreshIntervalSeconds: 2,
        EnableToastAlerts: true,
        EnableDailyHealthSummary: false,
        AutoSilentModeOnGame: false,
        EnableCleanupScheduler: false,
        ScheduledCleanupTime: TimeSpan.FromHours(3),
        Theme: "dark",
        ShowSystray: true,
        EnableOnboarding: false);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_SansFichier_RetourneLesValeursParDefaut()
    {
        var service = new SettingsService(_dir);

        var settings = service.Load();

        Assert.Equal(1, settings.RefreshIntervalSeconds);
        Assert.True(settings.EnableToastAlerts);
        Assert.Equal(90, settings.CpuAlertThresholdPercent);
        Assert.Equal(85, settings.CpuTempAlertThresholdC);
        Assert.Equal(82, settings.GpuTempAlertThresholdC);
        Assert.Equal(10, settings.DiskFreeAlertGb);
    }

    [Fact]
    public void SaveEtLoad_DepuisLeDisque_ConserveLesValeurs()
    {
        new SettingsService(_dir).Save(BaseSettings with { RefreshIntervalSeconds = 5, CpuAlertThresholdPercent = 75 });

        // Nouvelle instance pour forcer la relecture disque (pas le cache).
        var reloaded = new SettingsService(_dir).Load();

        Assert.Equal(5, reloaded.RefreshIntervalSeconds);
        Assert.Equal(75, reloaded.CpuAlertThresholdPercent);
    }

    [Fact]
    public void Save_ClampeLesValeursHorsBornes()
    {
        var service = new SettingsService(_dir);

        service.Save(BaseSettings with { RefreshIntervalSeconds = 999, MiniWidgetOpacity = 0.01, DiskFreeAlertGb = -4 });
        var settings = service.Load();

        Assert.Equal(10, settings.RefreshIntervalSeconds);
        Assert.Equal(0.35, settings.MiniWidgetOpacity);
        Assert.Equal(1, settings.DiskFreeAlertGb);
    }

    [Fact]
    public void Save_ChiffreLaCleApiSurDisque()
    {
        const string apiKey = "ma-cle-secrete-steamgriddb";
        new SettingsService(_dir).Save(BaseSettings with { SteamGridDbApiKey = apiKey });

        var raw = File.ReadAllText(Path.Combine(_dir, "settings.json"));

        Assert.DoesNotContain(apiKey, raw);
        Assert.Contains("dpapi:", raw);
    }

    [Fact]
    public void Load_DechiffreLaCleApiDepuisLeDisque()
    {
        const string apiKey = "ma-cle-secrete-steamgriddb";
        new SettingsService(_dir).Save(BaseSettings with { SteamGridDbApiKey = apiKey });

        var reloaded = new SettingsService(_dir).Load();

        Assert.Equal(apiKey, reloaded.SteamGridDbApiKey);
    }

    [Fact]
    public void Load_CleEnClairAncienFormat_EstConservee()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"RefreshIntervalSeconds":3,"EnableToastAlerts":true,"EnableDailyHealthSummary":true,"AutoSilentModeOnGame":false,"EnableCleanupScheduler":false,"ScheduledCleanupTime":"03:00:00","Theme":"dark","ShowSystray":true,"EnableOnboarding":true,"SteamGridDbApiKey":"cle-en-clair"}""");

        var settings = new SettingsService(_dir).Load();

        Assert.Equal("cle-en-clair", settings.SteamGridDbApiKey);
        Assert.Equal(3, settings.RefreshIntervalSeconds);
    }

    [Fact]
    public void Load_AncienFichierSansSeuils_AppliqueLesSeuilsParDefaut()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{"RefreshIntervalSeconds":3,"EnableToastAlerts":true,"EnableDailyHealthSummary":true,"AutoSilentModeOnGame":false,"EnableCleanupScheduler":false,"ScheduledCleanupTime":"03:00:00","Theme":"dark","ShowSystray":true,"EnableOnboarding":true}""");

        var settings = new SettingsService(_dir).Load();

        Assert.Equal(90, settings.CpuAlertThresholdPercent);
        Assert.Equal(10, settings.DiskFreeAlertGb);
    }

    [Fact]
    public void Load_FichierCorrompu_RetourneLesValeursParDefaut()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ pas du json ][");

        var settings = new SettingsService(_dir).Load();

        Assert.Equal(1, settings.RefreshIntervalSeconds);
    }
}
