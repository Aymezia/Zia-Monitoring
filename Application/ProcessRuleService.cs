using System.Diagnostics;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record ProcessRule(string ProcessName, string Priority)
{
    public string Label => $"{ProcessName} → {Priority}";
}

/// <summary>
/// Règles de priorité persistantes par exécutable, façon Process Lasso :
/// tant qu'une règle existe, le processus correspondant est remis à la
/// priorité voulue à chaque passage (réapplication automatique après
/// redémarrage du processus). Appliqué depuis la boucle de monitoring.
/// </summary>
public sealed class ProcessRuleService
{
    public static readonly string[] AllowedPriorities =
        ["Idle", "BelowNormal", "Normal", "AboveNormal", "High"];

    private static readonly TimeSpan ApplyInterval = TimeSpan.FromSeconds(5);

    private readonly string _rulesFile;
    private readonly object _gate = new();
    private List<ProcessRule> _rules = [];
    private DateTime _lastApply = DateTime.MinValue;

    public ProcessRuleService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _rulesFile = Path.Combine(dir, "process-rules.json");
        Load();
    }

    public IReadOnlyList<ProcessRule> GetRules()
    {
        lock (_gate)
            return _rules.ToList();
    }

    /// <summary>Ajoute ou remplace la règle pour cet exécutable. Retourne false si invalide.</summary>
    public bool AddOrUpdate(string processName, string priority)
    {
        processName = NormalizeName(processName);
        if (string.IsNullOrWhiteSpace(processName) || !AllowedPriorities.Contains(priority))
            return false;

        lock (_gate)
        {
            _rules.RemoveAll(r => r.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            _rules.Add(new ProcessRule(processName, priority));
            Save();
        }
        return true;
    }

    public void Remove(string processName)
    {
        lock (_gate)
        {
            if (_rules.RemoveAll(r => r.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)) > 0)
                Save();
        }
    }

    /// <summary>Applique les règles aux processus en cours (au plus toutes les 5 s).</summary>
    public void ApplyRules()
    {
        List<ProcessRule> rules;
        lock (_gate)
        {
            if (_rules.Count == 0 || DateTime.UtcNow - _lastApply < ApplyInterval)
                return;
            _lastApply = DateTime.UtcNow;
            rules = _rules.ToList();
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var rule = rules.FirstOrDefault(r =>
                    r.ProcessName.Equals(process.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (rule is null)
                    continue;

                try
                {
                    var target = Enum.Parse<ProcessPriorityClass>(rule.Priority);
                    if (process.PriorityClass != target)
                        process.PriorityClass = target;
                }
                catch
                {
                    // Processus protégé ou terminé : attendu.
                }
            }
        }
    }

    /// <summary>« discord.exe », « Discord » et « DISCORD.EXE » désignent le même processus.</summary>
    internal static string NormalizeName(string name)
    {
        name = name.Trim();
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_rulesFile))
                _rules = JsonSerializer.Deserialize<List<ProcessRule>>(File.ReadAllText(_rulesFile)) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des règles de processus impossible", ex);
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
            Infrastructure.AppLog.Error("Sauvegarde des règles de processus impossible", ex);
        }
    }
}
