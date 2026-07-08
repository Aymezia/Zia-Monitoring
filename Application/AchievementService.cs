using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record Achievement(string Id, string Title, string Description, int Target, int Progress)
{
    public bool IsUnlocked => Progress >= Target;
    public string ProgressLabel => IsUnlocked ? "Débloqué" : $"{Math.Min(Progress, Target)}/{Target}";
}

/// <summary>
/// Petit système de succès pour encourager la découverte des fonctionnalités :
/// des compteurs persistés (nombre de scans, boosts, etc.) comparés à des
/// seuils fixes. Purement cosmétique, aucune conséquence fonctionnelle.
/// </summary>
public sealed class AchievementService
{
    private static readonly (string Id, string Title, string Description, string CounterKey, int Target)[] Definitions =
    [
        ("first_launch", "Premier contact", "Ouvrir Zia Monitoring pour la première fois.", "app_launches", 1),
        ("booster", "Optimiseur", "Appliquer un profil de Boost.", "boosts_applied", 1),
        ("security_guard", "Gardien", "Lancer une analyse de sécurité.", "security_scans", 1),
        ("debloater", "Grand ménage", "Nettoyer 5 éléments via le Débloat.", "debloat_cleaned", 5),
        ("backup_master", "Prévoyant", "Sauvegarder ses saves de jeux.", "save_backups", 1),
        ("network_explorer", "Explorateur réseau", "Lancer un traceroute ou une mesure de région.", "network_probes", 1),
        ("librarian", "Bibliothécaire", "Avoir 10 jeux détectés sur ce PC.", "games_detected", 10),
        ("customizer", "Décorateur", "Changer le thème du widget.", "widget_theme_changes", 1),
        ("automator", "Automate", "Créer une règle personnalisée.", "custom_rules_created", 1),
        ("power_user", "Power user", "Utiliser 5 fonctionnalités différentes suivies par les succès.", "distinct_features_used", 5)
    ];

    private readonly string _stateFile;
    private readonly object _gate = new();
    private Dictionary<string, int> _counters = new();

    public AchievementService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "achievements.json");
        Load();
    }

    /// <summary>Incrémente un compteur (et le compteur agrégé de fonctionnalités distinctes utilisées).</summary>
    public void Increment(string counterKey, int amount = 1)
    {
        lock (_gate)
        {
            var wasZero = !_counters.TryGetValue(counterKey, out var before) || before == 0;
            _counters[counterKey] = _counters.GetValueOrDefault(counterKey) + amount;

            if (wasZero && counterKey != "distinct_features_used")
                _counters["distinct_features_used"] = _counters.GetValueOrDefault("distinct_features_used") + 1;

            Save();
        }
    }

    /// <summary>No-op si la valeur n'a pas changé (évite d'écrire sur disque à chaque cycle de monitoring).</summary>
    public void SetCounter(string counterKey, int value)
    {
        lock (_gate)
        {
            if (_counters.GetValueOrDefault(counterKey) == value)
                return;

            _counters[counterKey] = value;
            Save();
        }
    }

    public IReadOnlyList<Achievement> GetAchievements()
    {
        lock (_gate)
        {
            return Definitions
                .Select(d => new Achievement(d.Id, d.Title, d.Description, d.Target, _counters.GetValueOrDefault(d.CounterKey)))
                .ToList();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_stateFile))
                _counters = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_stateFile)) ?? new();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des succès impossible", ex);
            _counters = new();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_counters));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde des succès impossible", ex);
        }
    }
}
