using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class RecommendationEngine
{
    private readonly Queue<double> _cpuHistory = new();
    private readonly Queue<double> _weekTrend = new();

    public AnalysisReport Build(SystemSnapshot snapshot, PcProfile profile)
    {
        var recommendations = new List<Recommendation>();
        var assistedActions = BuildAssistedActions();
        var boostActions = BuildBoostActions();
        var alerts = new List<string>();

        var ramPct = snapshot.MemoryTotalMb <= 0 ? 0 : snapshot.MemoryUsedMb / snapshot.MemoryTotalMb * 100;
        AddHistory(_cpuHistory, snapshot.CpuPercent, 24);
        AddHistory(_weekTrend, BlendWeeklyTrend(snapshot.CpuPercent), 7);

        if (snapshot.CpuPercent > 85)
        {
            alerts.Add("CPU critical usage spike detected.");
            recommendations.Add(new Recommendation("High", "CPU saturation", "Load is frequently above 85%.", "Reduce startup load, check cooling, consider CPU upgrade."));
        }

        if (ramPct > 90)
        {
            alerts.Add("RAM usage is in critical zone.");
            recommendations.Add(new Recommendation("High", "RAM pressure", "Available memory is very low.", "Close heavy apps and target 32 GB for multitasking/gaming."));
        }

        var systemDisk = profile.Disks.FirstOrDefault(x => x.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
        if (systemDisk is not null)
        {
            var freePct = systemDisk.TotalGb <= 0 ? 100 : systemDisk.FreeGb / systemDisk.TotalGb * 100;
            if (freePct < 15)
            {
                alerts.Add("System disk free space below 15%.");
                recommendations.Add(new Recommendation("Medium", "Storage bottleneck risk", "System disk is nearly full.", "Clean temporary files and plan SSD capacity upgrade."));
            }
        }

        if (profile.TotalRamGb < 16)
        {
            recommendations.Add(new Recommendation("Medium", "RAM upgrade candidate", "Installed memory is below modern comfort level.", "Upgrade to 16 GB minimum, 32 GB if heavy multitasking."));
        }

        if (snapshot.CpuTemperatureC is null)
        {
            recommendations.Add(new Recommendation("Info", "Temperature source indisponible",
                "Les compteurs ACPI WMI n'exposent pas les zones thermiques sur ce systeme.",
                "Installez LibreHardwareMonitor et laissez-le tourner en tache de fond pour activer les temperatures."));
        }
        else if (snapshot.CpuTemperatureC > 90)
        {
            alerts.Add($"CPU en surchauffe: {snapshot.CpuTemperatureC:F0}°C !");
            recommendations.Add(new Recommendation("High", "Surchauffe CPU",
                $"Temperature a {snapshot.CpuTemperatureC:F0}°C — seuil critique depasse.",
                "Nettoyez les ventilateurs, remplacez la pate thermique, verifiez l'airflow du boitier."));
        }
        else if (snapshot.CpuTemperatureC > 75)
        {
            recommendations.Add(new Recommendation("Medium", "Temperatures CPU elevees",
                $"Temperature a {snapshot.CpuTemperatureC:F0}°C — zone de vigilance.",
                "Verifiez la ventilation et la qualite de la pate thermique."));
        }

        if (snapshot.GpuTemperatureC > 85)
        {
            alerts.Add($"GPU en surchauffe: {snapshot.GpuTemperatureC:F0}°C !");
            recommendations.Add(new Recommendation("High", "Surchauffe GPU",
                $"Temperature GPU a {snapshot.GpuTemperatureC:F0}°C.",
                "Nettoyez les ventilateurs GPU, verifiez les pads thermiques et l'airflow."));
        }

        if (snapshot.DiskIoReadMbps + snapshot.DiskIoWriteMbps > 400)
        {
            recommendations.Add(new Recommendation("Medium", "I/O disque intensif",
                $"Lecture+ecriture a {snapshot.DiskIoReadMbps + snapshot.DiskIoWriteMbps:F0} MB/s.",
                "Verifiez quel processus sature le disque via le gestionnaire des taches."));
        }

        var risk = ComputeRiskLevel(alerts.Count);
        var score = ComputeHealthScore(snapshot.CpuPercent, ramPct, alerts.Count);

        return new AnalysisReport(
            score,
            risk,
            recommendations,
            assistedActions,
            alerts,
            _cpuHistory.ToList(),
            _weekTrend.ToList(),
            boostActions);
    }

    private static List<AssistedAction> BuildAssistedActions()
    {
        return
        [
            new AssistedAction("Guided session", "Step-by-step optimization with safety checks.", "Self-guided"),
            new AssistedAction("Assisted intervention", "Diagnostics review and tailored execution plan.", "Assisted"),
            new AssistedAction("Premium boost", "Remote deep optimization and post-run report.", "Premium")
        ];
    }

    private static List<BoostActionItem> BuildBoostActions()
    {
        return
        [
            new BoostActionItem("Temp and cache cleanup", "Clean temporary folders and stale cache files.", "Low", false),
            new BoostActionItem("Startup optimization", "Disable high-impact startup entries after preview.", "Medium", true),
            new BoostActionItem("Service optimization", "Pause non-critical background services profile-wise.", "Medium", true),
            new BoostActionItem("Game profile", "Apply low-latency process priorities and background limits.", "Low", false)
        ];
    }

    private static int ComputeHealthScore(double cpu, double ram, int alertCount)
    {
        var score = 100;
        score -= (int)Math.Round(Math.Clamp(cpu - 60, 0, 40) * 0.6);
        score -= (int)Math.Round(Math.Clamp(ram - 70, 0, 30) * 0.8);
        score -= alertCount * 8;
        return Math.Clamp(score, 25, 99);
    }

    private static string ComputeRiskLevel(int alertCount)
    {
        if (alertCount >= 2)
        {
            return "High";
        }

        if (alertCount == 1)
        {
            return "Medium";
        }

        return "Low";
    }

    private static double BlendWeeklyTrend(double cpu)
    {
        var baseline = 48;
        var blend = baseline + ((cpu - baseline) * 0.35);
        return Math.Clamp(blend, 0, 100);
    }

    private static void AddHistory(Queue<double> queue, double value, int max)
    {
        queue.Enqueue(value);
        while (queue.Count > max)
        {
            queue.Dequeue();
        }
    }
}
