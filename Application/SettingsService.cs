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
        EnableOnboarding: true);

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
            return JsonSerializer.Deserialize<AppSettings>(json) ?? Default;
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
}
