using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ZiaMonitoring_App.ViewModels;

namespace ZiaMonitoring_App.Application;

public sealed class SupportExportService
{
    private readonly string _exportRoot;

    public SupportExportService()
    {
        _exportRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ZiaMonitoring", "SupportExports");
        Directory.CreateDirectory(_exportRoot);
    }

    public string ExportBundle(AppStateViewModel state)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(_exportRoot, $"bundle-{timestamp}");
        Directory.CreateDirectory(folder);

        var report = new
        {
            exportedAt = DateTime.Now,
            host = new
            {
                state.MachineName,
                state.OperatingSystem,
                state.Architecture,
                state.CpuModel,
                state.LogicalCores,
                state.InstalledRamGb
            },
            live = new
            {
                state.CpuPercent,
                state.MemoryUsedMb,
                state.MemoryTotalMb,
                state.MemoryPercent,
                state.CpuTempLabel,
                state.HealthScore,
                state.RiskLevel,
                state.LastUpdate
            },
            topProcesses = state.TopProcesses.ToList(),
            recommendations = state.Recommendations.ToList(),
            alerts = state.Alerts.ToList(),
            boostActions = state.BoostActions.ToList(),
            assistedActions = state.AssistedActions.ToList(),
            cpuHistory24h = state.CpuHistory24h.ToList(),
            cpuHistory7d = state.CpuHistory7d.ToList(),
            disks = state.Disks.ToList()
        };

        var jsonPath = Path.Combine(folder, "diagnostic-report.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, json, Encoding.UTF8);

        var summaryPath = Path.Combine(folder, "summary.txt");
        var summary = BuildSummary(state);
        File.WriteAllText(summaryPath, summary, Encoding.UTF8);

        var zipPath = Path.Combine(_exportRoot, $"zia-support-{timestamp}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(folder, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zipPath;
    }

    private static string BuildSummary(AppStateViewModel state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Zia Monitoring - Support Summary");
        sb.AppendLine($"Exported at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Machine: {state.MachineName}");
        sb.AppendLine($"OS: {state.OperatingSystem}");
        sb.AppendLine($"CPU: {state.CpuModel}");
        sb.AppendLine($"RAM: {state.InstalledRamGb:F1} GB");
        sb.AppendLine();
        sb.AppendLine($"Health: {state.HealthScore}/100 ({state.RiskLevel})");
        sb.AppendLine($"CPU: {state.CpuPercent:F1}%");
        sb.AppendLine($"RAM: {state.MemoryPercent:F1}%");
        sb.AppendLine($"Temp: {state.CpuTempLabel}");
        sb.AppendLine();

        if (state.Alerts.Count > 0)
        {
            sb.AppendLine("Alerts:");
            foreach (var alert in state.Alerts)
            {
                sb.AppendLine($"- {alert}");
            }
        }
        else
        {
            sb.AppendLine("Alerts: none");
        }

        return sb.ToString();
    }
}
