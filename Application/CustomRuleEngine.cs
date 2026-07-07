using System.Text.Json;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public enum RuleCondition { CpuAbove, RamAbove, CpuTempAbove, GpuTempAbove, DiskFreeBelow }

public enum RuleAction { Notify, RunDeepClean, OpenTaskManager }

public sealed record CustomRule(
    string Id,
    string Name,
    RuleCondition Condition,
    double Threshold,
    int SustainedMinutes,
    RuleAction Action,
    bool IsEnabled = true)
{
    private static string ConditionLabel(RuleCondition c) => c switch
    {
        RuleCondition.CpuAbove => "CPU au-dessus de",
        RuleCondition.RamAbove => "RAM au-dessus de",
        RuleCondition.CpuTempAbove => "Temp. CPU au-dessus de",
        RuleCondition.GpuTempAbove => "Temp. GPU au-dessus de",
        RuleCondition.DiskFreeBelow => "Disque C: libre sous",
        _ => c.ToString()
    };

    private static string Unit(RuleCondition c) => c == RuleCondition.DiskFreeBelow ? "GB"
        : c is RuleCondition.CpuTempAbove or RuleCondition.GpuTempAbove ? "°C" : "%";

    private static string ActionLabel(RuleAction a) => a switch
    {
        RuleAction.Notify => "Notifier",
        RuleAction.RunDeepClean => "Nettoyage temp",
        RuleAction.OpenTaskManager => "Ouvrir le Gestionnaire des tâches",
        _ => a.ToString()
    };

    public string Description =>
        $"{Name} : {ConditionLabel(Condition)} {Threshold:F0}{Unit(Condition)} pendant {SustainedMinutes} min → {ActionLabel(Action)}";
}

/// <summary>
/// Moteur de règles génériques « si condition soutenue pendant N minutes
/// alors action », au-delà des seuils fixes des alertes existantes. Chaque
/// règle a son propre minuteur de soutien (remis à zéro si la condition
/// cesse) et un cooldown de déclenchement pour éviter le spam.
/// </summary>
public sealed class CustomRuleEngine
{
    private static readonly TimeSpan FireCooldown = TimeSpan.FromMinutes(15);

    private readonly string _rulesFile;
    private readonly Dictionary<string, DateTime> _conditionSince = new();
    private readonly Dictionary<string, DateTime> _lastFired = new();
    private List<CustomRule> _rules = [];

    public CustomRuleEngine(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _rulesFile = Path.Combine(dir, "custom-rules.json");
        Load();
    }

    public IReadOnlyList<CustomRule> GetRules() => _rules.ToList();

    public CustomRule AddRule(string name, RuleCondition condition, double threshold, int sustainedMinutes, RuleAction action)
    {
        var rule = new CustomRule(Guid.NewGuid().ToString("N"), name, condition, threshold, Math.Max(0, sustainedMinutes), action);
        _rules.Add(rule);
        Save();
        return rule;
    }

    public void RemoveRule(string id)
    {
        if (_rules.RemoveAll(r => r.Id == id) > 0)
        {
            _conditionSince.Remove(id);
            _lastFired.Remove(id);
            Save();
        }
    }

    /// <summary>À appeler chaque cycle de monitoring. Retourne les règles qui viennent de se déclencher.</summary>
    public IReadOnlyList<CustomRule> Evaluate(SystemSnapshot snapshot, PcProfile profile)
    {
        var triggered = new List<CustomRule>();
        var now = DateTime.Now;

        foreach (var rule in _rules.Where(r => r.IsEnabled))
        {
            var conditionMet = EvaluateCondition(rule, snapshot, profile);

            if (!conditionMet)
            {
                _conditionSince.Remove(rule.Id);
                continue;
            }

            if (!_conditionSince.TryGetValue(rule.Id, out var since))
            {
                since = now;
                _conditionSince[rule.Id] = since;
            }

            if ((now - since).TotalMinutes < rule.SustainedMinutes)
                continue;

            if (_lastFired.TryGetValue(rule.Id, out var lastFired) && now - lastFired < FireCooldown)
                continue;

            _lastFired[rule.Id] = now;
            triggered.Add(rule);
        }

        return triggered;
    }

    internal static bool EvaluateCondition(CustomRule rule, SystemSnapshot snapshot, PcProfile profile)
    {
        return rule.Condition switch
        {
            RuleCondition.CpuAbove => snapshot.CpuPercent > rule.Threshold,
            RuleCondition.RamAbove => RamPercent(snapshot) > rule.Threshold,
            RuleCondition.CpuTempAbove => (snapshot.CpuTemperatureC ?? 0) > rule.Threshold,
            RuleCondition.GpuTempAbove => (snapshot.GpuTemperatureC ?? 0) > rule.Threshold,
            RuleCondition.DiskFreeBelow => profile.Disks.Any(d =>
                d.Name.StartsWith('C') && d.FreeGb < rule.Threshold),
            _ => false
        };
    }

    private static double RamPercent(SystemSnapshot snapshot) =>
        snapshot.MemoryTotalMb <= 0 ? 0 : snapshot.MemoryUsedMb / snapshot.MemoryTotalMb * 100;

    private void Load()
    {
        try
        {
            if (File.Exists(_rulesFile))
                _rules = JsonSerializer.Deserialize<List<CustomRule>>(File.ReadAllText(_rulesFile)) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des règles personnalisées impossible", ex);
            _rules = [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_rulesFile, JsonSerializer.Serialize(_rules, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde des règles personnalisées impossible", ex);
        }
    }
}
