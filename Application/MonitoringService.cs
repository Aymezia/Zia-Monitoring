using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Application;

public sealed class MonitoringService
{
    private readonly CpuUsageCollector _cpuCollector = new();
    private readonly MemoryCollector _memoryCollector = new();
    private readonly ProcessCollector _processCollector = new();
    private readonly TemperatureCollector _temperatureCollector = new();
    private readonly PcProfileCollector _profileCollector = new();
    private readonly GpuCollector _gpuCollector = new();
    private readonly NetworkCollector _networkCollector = new();
    private readonly PerCoreCpuCollector _perCoreCollector = new();
    private readonly DiskIoCollector _diskIoCollector = new();
    private readonly FanAndVramCollector _fanVramCollector = new();
    private readonly RecommendationEngine _engine = new();

    private PcProfile? _profileCache;

    public MonitoringFrame CaptureFrame()
    {
        var profile = _profileCache ??= _profileCollector.Collect();
        var cpu = _cpuCollector.GetCpuUsagePercent();
        var (usedMb, totalMb) = _memoryCollector.GetMemoryUsageMb();
        var temp = _temperatureCollector.GetCpuTemperatureC();
        var processes = _processCollector.GetTopProcesses(10);
        var (gpuTemp, gpuUsage) = _gpuCollector.GetGpuStats();
        var network = _networkCollector.GetStats();
        var perCore = _perCoreCollector.GetPerCoreUsage();
        var (diskRead, diskWrite) = _diskIoCollector.GetDiskIoMbps();
        var (vramUsed, vramTotal) = _fanVramCollector.GetVramUsage();
        var fanRpm = _fanVramCollector.GetFanSpeedRpm();

        var snapshot = new SystemSnapshot(
            DateTime.Now, cpu, usedMb, totalMb, temp,
            gpuTemp, gpuUsage, fanRpm,
            vramUsed, vramTotal,
            diskRead, diskWrite,
            perCore, network, processes);

        var analysis = _engine.Build(snapshot, profile);

        return new MonitoringFrame(snapshot, profile, analysis);
    }

    public void RefreshProfile()
    {
        _profileCache = null;
    }
}
