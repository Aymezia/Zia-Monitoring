using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;

namespace ZiaMonitoring_App.Application;

public sealed record ServiceDependencyInfo(string Name, string StartType, IReadOnlyList<string> DependsOn, IReadOnlyList<string> RequiredBy)
{
    public string Label => $"{Name} ({StartType})\nDépend de : {(DependsOn.Count == 0 ? "aucun" : string.Join(", ", DependsOn))}\nUtilisé par : {(RequiredBy.Count == 0 ? "aucun service" : string.Join(", ", RequiredBy))}";
}

public sealed record BootTimeSample(DateTime BootAt, double DurationSeconds)
{
    public string DurationLabel => $"{DurationSeconds:F0} s";
}

/// <summary>
/// Santé système : DISM/SFC en un clic (avec flux de sortie en direct),
/// dépendances de services (via sc.exe, pas de nouvelle dépendance), et
/// tendance du temps de démarrage lue depuis le journal d'événements que
/// Windows tient déjà lui-même (Diagnostics-Performance, Event ID 100) —
/// aucune trace ETW spéciale à déclencher, aucun redémarrage nécessaire.
/// </summary>
public sealed class SystemHealthService
{
    /// <summary>Lance DISM (ScanHealth puis RestoreHealth) puis SFC, en relayant chaque ligne de sortie.</summary>
    public async Task<(bool Success, string Summary)> RunHealthCheckAsync(Action<string> onOutputLine, CancellationToken ct = default)
    {
        try
        {
            onOutputLine("=== DISM /ScanHealth ===");
            var scan = await RunStreamingAsync("dism.exe", "/Online /Cleanup-Image /ScanHealth", onOutputLine, ct);

            onOutputLine("");
            onOutputLine("=== DISM /RestoreHealth ===");
            var restore = await RunStreamingAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", onOutputLine, ct);

            onOutputLine("");
            onOutputLine("=== SFC /scannow ===");
            var sfc = await RunStreamingAsync("sfc.exe", "/scannow", onOutputLine, ct);

            var success = scan == 0 && restore == 0 && sfc == 0;
            var summary = success
                ? "Analyse terminée : aucune corruption non réparée détectée."
                : $"Analyse terminée avec avertissements (codes: DISM scan={scan}, DISM restore={restore}, SFC={sfc}). Voir le détail ci-dessus.";
            return (success, summary);
        }
        catch (OperationCanceledException)
        {
            return (false, "Analyse annulée.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Vérification santé système (DISM/SFC) impossible", ex);
            return (false, ex.Message);
        }
    }

    private static Task<int> RunStreamingAsync(string fileName, string arguments, Action<string> onOutputLine, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>();
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.Unicode
        };

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onOutputLine(e.Data); };
        process.Exited += (_, _) =>
        {
            tcs.TrySetResult(process.ExitCode);
            process.Dispose();
        };

        using var registration = ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return tcs.Task;
    }

    public static ServiceDependencyInfo GetServiceDependencies(string serviceName)
    {
        var qcOutput = RunCaptureOutput("sc.exe", $"qc \"{serviceName}\"");
        var dependOutput = RunCaptureOutput("sc.exe", $"enumdepend \"{serviceName}\"");

        return ParseServiceDependencyInfo(serviceName, qcOutput, dependOutput);
    }

    /// <summary>Extraction pure du texte de sortie sc.exe, séparée pour être testable sans invoquer le service réel.</summary>
    internal static ServiceDependencyInfo ParseServiceDependencyInfo(string serviceName, string qcOutput, string dependOutput)
    {
        var startType = "Inconnu";
        var startLine = qcOutput.Split('\n').FirstOrDefault(l => l.Contains("START_TYPE", StringComparison.OrdinalIgnoreCase));
        if (startLine is not null)
        {
            var idx = startLine.IndexOf(':');
            if (idx >= 0)
                startType = startLine[(idx + 1)..].Trim();
        }

        var dependsOn = new List<string>();
        var dependencyLine = qcOutput.Split('\n').FirstOrDefault(l => l.Contains("DEPENDENCIES", StringComparison.OrdinalIgnoreCase));
        if (dependencyLine is not null)
        {
            var idx = dependencyLine.IndexOf(':');
            var firstValue = idx >= 0 ? dependencyLine[(idx + 1)..].Trim() : string.Empty;
            if (!string.IsNullOrWhiteSpace(firstValue) && !firstValue.Equals("(none)", StringComparison.OrdinalIgnoreCase))
                dependsOn.Add(firstValue);
        }

        var requiredBy = dependOutput.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            .Select(l => l["SERVICE_NAME:".Length..].Trim())
            .Where(n => n.Length > 0)
            .ToList();

        return new ServiceDependencyInfo(serviceName, startType, dependsOn, requiredBy);
    }

    /// <summary>Lit le temps de démarrage mesuré par Windows lui-même (aucune trace spéciale à activer).</summary>
    public static IReadOnlyList<BootTimeSample> GetBootTimeHistory(int maxEntries = 20)
    {
        var results = new List<BootTimeSample>();

        try
        {
            var query = new EventLogQuery(
                "Microsoft-Windows-Diagnostics-Performance/Operational",
                PathType.LogName,
                "*[System[(EventID=100)]]");

            using var reader = new EventLogReader(query);
            EventRecord? record;
            while (results.Count < maxEntries && (record = reader.ReadEvent()) is not null)
            {
                using (record)
                {
                    var durationMs = record.Properties.Count > 9
                        ? record.Properties[9].Value as uint?
                        : null;

                    if (record.TimeCreated is { } timeCreated && durationMs is > 0)
                        results.Add(new BootTimeSample(timeCreated, durationMs.Value / 1000.0));
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'historique de démarrage (journal Windows) impossible", ex);
        }

        return results;
    }

    private static string RunCaptureOutput(string fileName, string arguments, int timeoutMs = 8000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de démarrer {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return output;
    }
}
