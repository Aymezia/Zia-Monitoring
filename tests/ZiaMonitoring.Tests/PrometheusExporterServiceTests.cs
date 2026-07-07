using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class PrometheusExporterServiceTests
{
    private static MonitoringFrame Frame(double cpu = 42.5, double? cpuTemp = 65, double? gpuTemp = null, double? gpuUsage = null)
    {
        var snapshot = new SystemSnapshot(
            DateTime.Now, cpu, 8000, 16000, cpuTemp, gpuTemp, gpuUsage, FanSpeedRpm: 1200,
            VramUsedMb: 1000, VramTotalMb: 8000, DiskIoReadMbps: 5, DiskIoWriteMbps: 3, EstimatedFps: 144,
            PerCoreCpu: [], Network: new NetworkStats(100, 500, 20), ActiveTcpConnections: [],
            TopNetworkProcesses: [], GameServerLatencies: [], TopProcesses: []);

        var analysis = new AnalysisReport(85, "Low", [], [], [], [], [], []);
        var profile = new PcProfile("PC", "Windows 11", "x64", "CPU", "GPU", "Board", "1.0",
            16, 32, 500, TimeSpan.Zero, [], []);

        return new MonitoringFrame(snapshot, profile, analysis);
    }

    [Fact]
    public void BuildExposition_ContientLesMetriquesDeBase()
    {
        var text = PrometheusExporterService.BuildExposition(Frame());

        Assert.Contains("zia_cpu_percent 42.5", text);
        Assert.Contains("zia_memory_used_mb 8000", text);
        Assert.Contains("zia_health_score 85", text);
        Assert.Contains("# TYPE zia_cpu_percent gauge", text);
        Assert.Contains("# HELP zia_cpu_percent", text);
    }

    [Fact]
    public void BuildExposition_TemperaturesNulles_OmetLesMetriquesCorrespondantes()
    {
        var text = PrometheusExporterService.BuildExposition(Frame(cpuTemp: null, gpuTemp: null, gpuUsage: null));

        Assert.DoesNotContain("zia_cpu_temperature_celsius", text);
        Assert.DoesNotContain("zia_gpu_temperature_celsius", text);
        Assert.DoesNotContain("zia_gpu_usage_percent", text);
    }

    [Fact]
    public void BuildExposition_TemperaturesPresentes_LesInclut()
    {
        var text = PrometheusExporterService.BuildExposition(Frame(cpuTemp: 72.3, gpuTemp: 68.1, gpuUsage: 55));

        Assert.Contains("zia_cpu_temperature_celsius 72.3", text);
        Assert.Contains("zia_gpu_temperature_celsius 68.1", text);
        Assert.Contains("zia_gpu_usage_percent 55", text);
    }

    [Fact]
    public void BuildExposition_UtiliseLePointCommeSeparateurDecimal()
    {
        var text = PrometheusExporterService.BuildExposition(Frame(cpu: 12.34));

        Assert.Contains("12.34", text);
        Assert.DoesNotContain("12,34", text);
    }
}
