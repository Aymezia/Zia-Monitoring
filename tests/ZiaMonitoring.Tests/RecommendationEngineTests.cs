using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class RecommendationEngineTests
{
    private static SystemSnapshot Snapshot(
        double cpuPercent = 20,
        double memoryUsedMb = 4_000,
        double memoryTotalMb = 16_000,
        double? cpuTempC = 45,
        double? gpuTempC = 40,
        double diskReadMbps = 5,
        double diskWriteMbps = 5)
    {
        return new SystemSnapshot(
            DateTime.Now, cpuPercent, memoryUsedMb, memoryTotalMb,
            cpuTempC, gpuTempC, GpuUsagePercent: 10, FanSpeedRpm: 1200,
            VramUsedMb: 1000, VramTotalMb: 8000,
            diskReadMbps, diskWriteMbps,
            EstimatedFps: 0,
            PerCoreCpu: [],
            Network: new NetworkStats(0, 0, 20),
            ActiveTcpConnections: [],
            TopNetworkProcesses: [],
            GameServerLatencies: [],
            TopProcesses: []);
    }

    private static PcProfile Profile(double totalRamGb = 32, double diskFreeGb = 200, double diskTotalGb = 500)
    {
        return new PcProfile(
            "TEST-PC", "Windows 11", "x64", "Test CPU", "Test GPU", "Test Board", "1.0",
            LogicalCores: 16, TotalRamGb: totalRamGb, TotalDiskGb: diskTotalGb,
            Uptime: TimeSpan.FromHours(2),
            Disks: [new DiskProfile("C:\\", "NTFS", diskTotalGb, diskFreeGb)],
            InstalledGames: []);
    }

    [Fact]
    public void Build_SystemeSain_AucuneAlerteEtRisqueFaible()
    {
        var engine = new RecommendationEngine();

        var report = engine.Build(Snapshot(), Profile());

        Assert.Empty(report.Alerts);
        Assert.Equal("Low", report.RiskLevel);
        Assert.InRange(report.HealthScore, 25, 99);
    }

    [Fact]
    public void Build_CpuSature_GenereAlerteEtRecommandationHaute()
    {
        var engine = new RecommendationEngine();

        var report = engine.Build(Snapshot(cpuPercent: 95), Profile());

        Assert.Contains(report.Alerts, a => a.Contains("CPU", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Recommendations, r => r.Priority == "High");
        Assert.NotEqual("Low", report.RiskLevel);
    }

    [Fact]
    public void Build_RamCritique_GenereAlerte()
    {
        var engine = new RecommendationEngine();

        var report = engine.Build(Snapshot(memoryUsedMb: 15_000, memoryTotalMb: 16_000), Profile());

        Assert.Contains(report.Alerts, a => a.Contains("RAM", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_SurchauffeCpu_GenereAlerteEtRecommandation()
    {
        var engine = new RecommendationEngine();

        var report = engine.Build(Snapshot(cpuTempC: 95), Profile());

        Assert.Contains(report.Alerts, a => a.Contains("surchauffe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(report.Recommendations, r => r.Title.Contains("Surchauffe", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_TemperatureIndisponible_RecommandationInfo()
    {
        var engine = new RecommendationEngine();

        var report = engine.Build(Snapshot(cpuTempC: null), Profile());

        Assert.Contains(report.Recommendations, r => r.Priority == "Info");
    }

    [Fact]
    public void Build_DeuxAlertes_RisqueEleve()
    {
        var engine = new RecommendationEngine();

        // CPU saturé + RAM critique = 2 alertes minimum.
        var report = engine.Build(Snapshot(cpuPercent: 95, memoryUsedMb: 15_500, memoryTotalMb: 16_000), Profile());

        Assert.True(report.Alerts.Count >= 2);
        Assert.Equal("High", report.RiskLevel);
    }

    [Fact]
    public void Build_HistoriqueCpu_PlafonneA24Echantillons()
    {
        var engine = new RecommendationEngine();
        AnalysisReport report = engine.Build(Snapshot(), Profile());

        for (var i = 0; i < 40; i++)
            report = engine.Build(Snapshot(cpuPercent: i), Profile());

        Assert.Equal(24, report.CpuHistory24h.Count);
        Assert.Equal(7, report.CpuHistory7d.Count);
    }

    [Fact]
    public void Build_ScoreSante_ResteDansLesBornes()
    {
        var engine = new RecommendationEngine();

        var worst = engine.Build(Snapshot(cpuPercent: 100, memoryUsedMb: 16_000, memoryTotalMb: 16_000, cpuTempC: 100, gpuTempC: 95), Profile(diskFreeGb: 5));
        var best = engine.Build(Snapshot(cpuPercent: 0, memoryUsedMb: 0), Profile());

        Assert.InRange(worst.HealthScore, 25, 99);
        Assert.InRange(best.HealthScore, 25, 99);
        Assert.True(best.HealthScore >= worst.HealthScore);
    }
}
