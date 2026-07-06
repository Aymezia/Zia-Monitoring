using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure.Collectors;

namespace ZiaMonitoring_App.Application;

public sealed class MonitoringService : IDisposable
{
    private readonly CpuUsageCollector _cpuCollector = new();
    private readonly MemoryCollector _memoryCollector = new();
    private readonly ProcessCollector _processCollector = new();
    private readonly PcProfileCollector _profileCollector = new();
    private readonly NetworkCollector _networkCollector = new();
    private readonly PerCoreCpuCollector _perCoreCollector = new();
    private readonly DiskIoCollector _diskIoCollector = new();
    private readonly FanAndVramCollector _fanVramCollector = new();
    private readonly HardwareMonitor _hardwareMonitor = new();
    private readonly EstimatedFpsCollector _estimatedFpsCollector = new();
    private readonly NetworkConnectionCollector _networkConnectionCollector = new();
    private readonly GameServerLatencyCollector _gameServerLatencyCollector = new();
    private readonly RecommendationEngine _engine = new();

    private PcProfile? _profileCache;

    public MonitoringFrame CaptureFrame()
    {
        var profile = _profileCache ??= _profileCollector.Collect();
        var cpu = _cpuCollector.GetCpuUsagePercent();
        var (usedMb, totalMb) = _memoryCollector.GetMemoryUsageMb();
        var processes = _processCollector.GetTopProcesses(10);
        var network = _networkCollector.GetStats();
        var perCore = _perCoreCollector.GetPerCoreUsage();
        var (diskRead, diskWrite) = _diskIoCollector.GetDiskIoMbps();
        var (vramUsed, vramTotal) = _fanVramCollector.GetVramUsage();
        var estimatedFps = _estimatedFpsCollector.GetEstimatedFps();
        var activeConnections = _networkConnectionCollector.GetActiveTcpConnections();
        var networkProcesses = _networkConnectionCollector.GetTopNetworkProcesses(activeConnections);
        var gameLatencies = _gameServerLatencyCollector.GetGameServerLatencies();

        // Real hardware readings from LibreHardwareMonitor
        var hw = _hardwareMonitor.Read();

        var snapshot = new SystemSnapshot(
            DateTime.Now, cpu, usedMb, totalMb,
            hw.CpuTemperatureC,
            hw.GpuTemperatureC,
            hw.GpuUsagePercent,
            hw.FanSpeedRpm,
            vramUsed, vramTotal,
            diskRead, diskWrite,
            estimatedFps,
            perCore, network,
            activeConnections, networkProcesses, gameLatencies,
            processes);

        var analysis = _engine.Build(snapshot, profile);

        return new MonitoringFrame(snapshot, profile, analysis);
    }

    public void RefreshProfile()
    {
        _profileCache = null;
    }

    public void Dispose()
    {
        _hardwareMonitor.Dispose();
        _estimatedFpsCollector.Dispose();
    }
}
