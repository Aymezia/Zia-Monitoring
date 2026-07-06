using System.Text.Json;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class SettingsService
{
    private readonly string _settingsFile;
    private readonly object _gate = new();
    private AppSettings? _cached;

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

    /// <summary>
    /// Retourne les paramètres depuis le cache mémoire ; le disque n'est lu
    /// qu'au premier appel (la boucle de monitoring appelle Load chaque cycle).
    /// </summary>
    public AppSettings Load()
    {
        lock (_gate)
        {
            return _cached ??= LoadFromDisk();
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            _cached = Clamp(settings);
            try
            {
                var json = JsonSerializer.Serialize(_cached, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Error("Sauvegarde des paramètres impossible", ex);
            }
        }
    }

    private AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsFile))
                return Default;

            var json = File.ReadAllText(_settingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? Default;
            return Normalize(settings, json);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des paramètres impossible, valeurs par défaut utilisées", ex);
            return Default;
        }
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

        return Clamp(settings);
    }

    private static AppSettings Clamp(AppSettings settings)
    {
        return settings with
        {
            RefreshIntervalSeconds = Math.Clamp(settings.RefreshIntervalSeconds, 1, 10),
            MiniWidgetOpacity = Math.Clamp(settings.MiniWidgetOpacity, 0.35, 1.0),
            SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty
        };
    }
}
