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
    private readonly ThrottlingDetector _throttlingDetector = new();
    private readonly PresentMonService _presentMon;
    private readonly SettingsService _settings;

    private PcProfile? _profileCache;

    public MonitoringService(PresentMonService presentMon, SettingsService settings)
    {
        _presentMon = presentMon;
        _settings = settings;
    }

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
        // FPS réels (PresentMon) si une capture est active, sinon estimation GPU Engine.
        var estimatedFps = _presentMon.CurrentFps ?? _estimatedFpsCollector.GetEstimatedFps();
        var activeConnections = _networkConnectionCollector.GetActiveTcpConnections();
        var networkProcesses = _networkConnectionCollector.GetTopNetworkProcesses(activeConnections);
        var gameLatencies = _gameServerLatencyCollector.GetGameServerLatencies();

        // Real hardware readings from LibreHardwareMonitor.
        // Opt-in uniquement : ouvrir le driver installe un composant kernel
        // (WinRing0) que Defender supprime via son blocklist de drivers vulnérables.
        HardwareReadings hw;
        if (_settings.Load().EnableHardwareSensors)
        {
            _hardwareMonitor.EnableSensors();
            hw = _hardwareMonitor.Read();
        }
        else
        {
            hw = new HardwareReadings(null, null, null, null, null, null);
        }

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
            processes,
            hw.CpuClockMhz, hw.GpuClockMhz);

        var analysis = _engine.Build(snapshot, profile);

        var throttlingToast = _throttlingDetector.Update(
            hw.CpuTemperatureC, hw.CpuClockMhz, cpu,
            hw.GpuTemperatureC, hw.GpuClockMhz, hw.GpuUsagePercent);

        if (_throttlingDetector.ActiveAlert is { } throttlingAlert)
            analysis = analysis with { Alerts = [.. analysis.Alerts, throttlingAlert] };

        var connectionKind = NetworkCollector.DetectActiveConnectionKind();

        return new MonitoringFrame(snapshot, profile, analysis, throttlingToast, connectionKind);
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
