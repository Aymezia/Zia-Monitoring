using System.Security.Cryptography;
using System.Text;
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

    /// <param name="settingsDirectory">Répertoire de stockage (tests) ; %LOCALAPPDATA%\ZiaMonitoring par défaut.</param>
    public SettingsService(string? settingsDirectory = null)
    {
        var dir = settingsDirectory ?? Path.Combine(
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
                // La clé API et le mot de passe OBS ne sont jamais écrits en clair sur disque.
                var persisted = _cached with
                {
                    SteamGridDbApiKey = Protect(_cached.SteamGridDbApiKey),
                    ObsPassword = Protect(_cached.ObsPassword)
                };
                var json = JsonSerializer.Serialize(persisted, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFile, json);
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Error("Sauvegarde des paramètres impossible", ex);
            }
        }
    }

    /// <summary>Exporte tous les réglages (clé API/mot de passe OBS chiffrés DPAPI, comme sur disque).</summary>
    public void ExportToFile(string path)
    {
        lock (_gate)
        {
            var current = _cached ?? LoadFromDisk();
            var exported = current with
            {
                SteamGridDbApiKey = Protect(current.SteamGridDbApiKey),
                ObsPassword = Protect(current.ObsPassword)
            };
            File.WriteAllText(path, JsonSerializer.Serialize(exported, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>Importe des réglages exportés et les applique immédiatement (déchiffrement DPAPI, mêmes bornes que Save).</summary>
    public AppSettings ImportFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? Default;
        settings = settings with
        {
            SteamGridDbApiKey = Unprotect(settings.SteamGridDbApiKey ?? string.Empty),
            ObsPassword = Unprotect(settings.ObsPassword ?? string.Empty)
        };
        settings = Clamp(settings);
        Save(settings);
        return settings;
    }

    private AppSettings LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_settingsFile))
                return Default;

            var json = File.ReadAllText(_settingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? Default;
            settings = settings with
            {
                SteamGridDbApiKey = Unprotect(settings.SteamGridDbApiKey ?? string.Empty),
                ObsPassword = Unprotect(settings.ObsPassword ?? string.Empty)
            };
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
            SteamGridDbApiKey = settings.SteamGridDbApiKey ?? string.Empty,
            ObsPassword = settings.ObsPassword ?? string.Empty,
            ObsHost = string.IsNullOrWhiteSpace(settings.ObsHost) ? "localhost" : settings.ObsHost,
            ObsPort = settings.ObsPort <= 0 ? 4455 : settings.ObsPort,
            CpuAlertThresholdPercent = Math.Clamp(settings.CpuAlertThresholdPercent, 50, 100),
            CpuTempAlertThresholdC = Math.Clamp(settings.CpuTempAlertThresholdC, 50, 105),
            GpuTempAlertThresholdC = Math.Clamp(settings.GpuTempAlertThresholdC, 50, 105),
            DiskFreeAlertGb = Math.Clamp(settings.DiskFreeAlertGb, 1, 100),
            WeeklyPlaytimeGoalHours = Math.Clamp(settings.WeeklyPlaytimeGoalHours, 0, 100)
        };
    }

    private const string DpapiPrefix = "dpapi:";

    /// <summary>Chiffre la valeur via DPAPI (portée utilisateur Windows).</summary>
    private static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        try
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plainText),
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Chiffrement DPAPI de la clé API impossible", ex);
            return string.Empty;
        }
    }

    /// <summary>
    /// Déchiffre une valeur DPAPI ; une valeur sans préfixe (ancien format en
    /// clair) est retournée telle quelle et sera chiffrée à la prochaine sauvegarde.
    /// </summary>
    private static string Unprotect(string storedValue)
    {
        if (string.IsNullOrEmpty(storedValue) || !storedValue.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return storedValue;

        try
        {
            var raw = Convert.FromBase64String(storedValue[DpapiPrefix.Length..]);
            var decrypted = ProtectedData.Unprotect(raw, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Déchiffrement DPAPI de la clé API impossible (clé réinitialisée)", ex);
            return string.Empty;
        }
    }
}
