using System.Text.Json;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class SettingsService
{
    private readonly string _settingsFile;

    private static readonly AppSettings Default = new(
        RefreshIntervalSeconds: 1,
        EnableToastAlerts: true,
        EnableDailyHealthSummary: true,
        AutoSilentModeOnGame: false,
        EnableCleanupScheduler: false,
        ScheduledCleanupTime: TimeSpan.FromHours(3),
        Theme: "dark",
        ShowSystray: true,
        EnableOnboarding: true,
        EnableAutoStart: false,
        EnableGlobalHotkey: true,
        EnableGameOverlay: true,
        EnableMiniWidget: false,
        MiniWidgetOpacity: 0.88,
        SteamGridDbApiKey: "");

    public SettingsService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _settingsFile = Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFile))
                return Default;

            var json = File.ReadAllText(_settingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? Default;
            return Normalize(settings, json);
        }
        catch
        {
            return Default;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFile, json);
        }
        catch { }
    }

    private static AppSettings Normalize(AppSettings settings, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty(nameof(AppSettings.EnableGlobalHotkey), out _))
            settings = settings with { EnableGlobalHotkey = Default.EnableGlobalHotkey };

        if (!root.TryGetProperty(nameof(AppSettings.EnableGameOverlay), out _))
            settings = settings with { EnableGameOverlay = Default.EnableGameOverlay };

        if (!root.TryGetProperty(nameof(AppSettings.MiniWidgetOpacity), out _))
            settings = settings with { MiniWidgetOpacity = Default.MiniWidgetOpacity };

        if (!root.TryGetProperty(nameof(AppSettings.ShowSystray), out _))
            settings = settings with { ShowSystray = Default.ShowSystray };

        return settings with
        {
            RefreshIntervalSeconds = Math.Clamp(settings.RefreshIntervalSeconds, 1, 10),
            MiniWidgetOpacity = Math.Clamp(settings.MiniWidgetOpacity, 0.35, 1.0),
            SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty
        };
    }
}
