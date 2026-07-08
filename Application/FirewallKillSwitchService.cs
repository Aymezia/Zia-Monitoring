using System.Diagnostics;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

public sealed record BlockedApp(string ExePath, string RuleName)
{
    public string Label => ExePath;
}

/// <summary>
/// Kill switch réseau par application : règle de pare-feu Windows sortante
/// (netsh advfirewall) bloquant tout le trafic d'un exécutable, réversible en
/// un clic. Les règles créées sont préfixées et suivies dans un fichier
/// d'état local — seules celles créées ici sont supprimables ici.
/// </summary>
public sealed class FirewallKillSwitchService
{
    private const string RulePrefix = "Zia Kill Switch - ";

    private readonly string _stateFile;
    private readonly object _gate = new();
    private List<BlockedApp> _blocked = [];

    public FirewallKillSwitchService(string? storageDirectory = null)
    {
        var dir = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring");
        Directory.CreateDirectory(dir);
        _stateFile = Path.Combine(dir, "killswitch-rules.json");
        Load();
    }

    public IReadOnlyList<BlockedApp> GetBlockedApps()
    {
        lock (_gate) return _blocked.ToList();
    }

    public (bool Success, string Message) Block(string exePath)
    {
        exePath = exePath.Trim().Trim('"');
        if (!File.Exists(exePath))
            return (false, "Exécutable introuvable.");

        lock (_gate)
        {
            if (_blocked.Any(b => b.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                return (true, "Déjà bloqué.");

            var ruleName = BuildRuleName(exePath);
            var (code, _, err) = RunNetsh($"advfirewall firewall add rule name=\"{ruleName}\" dir=out action=block program=\"{exePath}\" enable=yes");
            if (code != 0)
                return (false, $"Échec de la création de la règle : {FirstNonEmpty(err)}");

            _blocked.Add(new BlockedApp(exePath, ruleName));
            Save();
            return (true, $"Accès internet bloqué pour {Path.GetFileName(exePath)}. Redémarrez l'application ciblée pour couper ses connexions déjà ouvertes.");
        }
    }

    public (bool Success, string Message) Unblock(string exePath)
    {
        lock (_gate)
        {
            var entry = _blocked.FirstOrDefault(b => b.ExePath.Equals(exePath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                return (true, "Déjà débloqué.");

            var (code, _, err) = RunNetsh($"advfirewall firewall delete rule name=\"{entry.RuleName}\"");
            if (code != 0)
                return (false, $"Échec de la suppression de la règle : {FirstNonEmpty(err)}");

            _blocked.Remove(entry);
            Save();
            return (true, $"Accès internet rétabli pour {Path.GetFileName(exePath)}.");
        }
    }

    /// <summary>Nom unique et reconnaissable ; le hash évite les collisions entre exe homonymes.</summary>
    internal static string BuildRuleName(string exePath)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(exePath.ToLowerInvariant())))[..8];
        return $"{RulePrefix}{Path.GetFileName(exePath)} [{hash}]";
    }

    private static (int ExitCode, string StdOut, string StdErr) RunNetsh(string arguments)
    {
        var psi = new ProcessStartInfo("netsh", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de démarrer netsh.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(8000);
        return (process.ExitCode, output, error);
    }

    private static string FirstNonEmpty(string text) => text.Trim() is { Length: > 0 } t ? t : "erreur inconnue.";

    private void Load()
    {
        try
        {
            if (File.Exists(_stateFile))
                _blocked = JsonSerializer.Deserialize<List<BlockedApp>>(File.ReadAllText(_stateFile)) ?? [];
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'état du kill switch impossible", ex);
            _blocked = [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_stateFile, JsonSerializer.Serialize(_blocked, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Sauvegarde de l'état du kill switch impossible", ex);
        }
    }
}
