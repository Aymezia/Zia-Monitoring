using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.ViewModels;

public sealed partial class AppStateViewModel : ObservableObject
{
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _memoryUsedMb;
    [ObservableProperty] private double _memoryTotalMb;
    [ObservableProperty] private string _cpuTempLabel = "N/A";
    [ObservableProperty] private double _cpuTempPercent;
    [ObservableProperty] private string _gpuTempLabel = "N/A";
    [ObservableProperty] private double _gpuTempPercent;
    [ObservableProperty] private string _gpuUsageLabel = "N/A";
    [ObservableProperty] private double _gpuUsagePercent;
    [ObservableProperty] private string _fanLabel = "N/A";
    [ObservableProperty] private string _vramLabel = "N/A";
    [ObservableProperty] private double _vramTotalMb;
    [ObservableProperty] private string _diskIoLabel = "N/A";
    [ObservableProperty] private string _networkLabel = "-";
    [ObservableProperty] private string _connectionTypeLabel = "Connexion : -";
    [ObservableProperty] private string _pingLabel = "-";
    [ObservableProperty] private string _estimatedFpsLabel = "FPS: N/A";
    [ObservableProperty] private string _activeGameServerLabel = "Serveur: N/A";
    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private string _riskLevel = "Low";
    [ObservableProperty] private string _machineName = string.Empty;
    [ObservableProperty] private string _operatingSystem = string.Empty;
    [ObservableProperty] private string _architecture = string.Empty;
    [ObservableProperty] private string _cpuModel = string.Empty;
    [ObservableProperty] private string _gpuModel = string.Empty;
    [ObservableProperty] private string _motherboard = string.Empty;
    [ObservableProperty] private string _biosVersion = string.Empty;
    [ObservableProperty] private int _logicalCores;
    [ObservableProperty] private double _installedRamGb;
    [ObservableProperty] private double _totalDiskGb;
    [ObservableProperty] private string _uptimeLabel = "-";
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private int _optimizationProgress;
    [ObservableProperty] private string _optimizationStageText = "Pret";
    [ObservableProperty] private string _lastUpdate = "-";
    [ObservableProperty] private string _activeGameLabel = "Aucun jeu detecte";
    [ObservableProperty] private bool _isGameActive;
    [ObservableProperty] private bool _noAlerts = true;

    private DateTime _lastDiskHistorySample = DateTime.MinValue;

    public AppStateViewModel()
    {
        Cpu24Series =
        [
            new LineSeries<double>
            {
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.7
            }
        ];

        Cpu7Series =
        [
            new LineSeries<double>
            {
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.7
            }
        ];

        CpuYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                MinStep = 10
            }
        ];

        Cpu24XAxes =
        [
            new Axis
            {
                Labels = Enumerable.Range(1, 24).Select(i => i.ToString()).ToArray()
            }
        ];

        Cpu7XAxes =
        [
            new Axis
            {
                Labels = new[] { "J1", "J2", "J3", "J4", "J5", "J6", "J7" }
            }
        ];

        GpuSeries =
        [
            new LineSeries<double>
            {
                Name = "GPU %",
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.7,
                Fill = null
            }
        ];

        TempSeries =
        [
            new LineSeries<double>
            {
                Name = "CPU °C",
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.5,
                Fill = null
            },
            new LineSeries<double>
            {
                Name = "GPU °C",
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.5,
                Fill = null
            }
        ];

        DiskIoSeries =
        [
            new LineSeries<double>
            {
                Name = "Lecture MB/s",
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.5,
                Fill = null
            },
            new LineSeries<double>
            {
                Name = "Ecriture MB/s",
                Values = Array.Empty<double>(),
                GeometrySize = 0,
                LineSmoothness = 0.5,
                Fill = null
            }
        ];

        GamePingSeries =
        [
            new LineSeries<double> { Name = "Riot", Values = Array.Empty<double>(), GeometrySize = 0, LineSmoothness = 0.4, Fill = null },
            new LineSeries<double> { Name = "Valve", Values = Array.Empty<double>(), GeometrySize = 0, LineSmoothness = 0.4, Fill = null },
            new LineSeries<double> { Name = "EA", Values = Array.Empty<double>(), GeometrySize = 0, LineSmoothness = 0.4, Fill = null },
            new LineSeries<double> { Name = "Epic", Values = Array.Empty<double>(), GeometrySize = 0, LineSmoothness = 0.4, Fill = null }
        ];

        GpuYAxes = [new Axis { MinLimit = 0, MaxLimit = 100, MinStep = 10 }];
        TempYAxes = [new Axis { MinLimit = 0, MaxLimit = 110, MinStep = 10 }];
        DiskYAxes = [new Axis { MinLimit = 0, MinStep = 10 }];
        PingYAxes = [new Axis { MinLimit = 0, MinStep = 20 }];
    }

    public double MemoryPercent => MemoryTotalMb <= 0 ? 0 : (MemoryUsedMb / MemoryTotalMb) * 100;
    public string CpuPercentLabel => $"{CpuPercent:F1}%";
    public string MemoryPercentLabel => $"{MemoryPercent:F1}%";
    public string MemoryDetailLabel => MemoryTotalMb <= 0
        ? "-"
        : $"{MemoryUsedMb / 1024:F1} / {MemoryTotalMb / 1024:F1} GB";
    public string HealthScoreLabel => $"{HealthScore}/100";
    public string RiskLevelLabel => $"Risk: {RiskLevel}";
    public string LastUpdateLabel => $"Dernière mise à jour : {LastUpdate}";
    public string HealthScoreLongLabel => $"Score: {HealthScore}/100";
    public string HealthRiskLongLabel => $"Risque: {RiskLevel}";
    public string MachineLabel => $"Machine: {MachineName}";
    public string OsLabel => $"OS: {OperatingSystem}";
    public string ArchitectureLabel => $"Architecture: {Architecture}";
    public string CpuLabel => $"CPU: {CpuModel}";
    public string GpuLabel => $"GPU: {GpuModel}";
    public string MotherboardLabel => $"Carte mere: {Motherboard}";
    public string BiosLabel => $"BIOS: {BiosVersion}";
    public string InstalledRamLabel => $"RAM installee: {InstalledRamGb:F1} GB";
    public string TotalDiskLabel => $"Stockage total: {TotalDiskGb:F1} GB";
    public string GamesCountLabel => $"Jeux detectes: {InstalledGames.Count}";
    public string OptimizationProgressLabel => $"Progression: {OptimizationProgress}%";

    public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();
    public ObservableCollection<DiskProfile> Disks { get; } = new();
    public ObservableCollection<GameInstallation> InstalledGames { get; } = new();
    public ObservableCollection<PerCoreCpuUsage> PerCoreCpu { get; } = new();
    public ObservableCollection<Recommendation> Recommendations { get; } = new();
    public ObservableCollection<BoostActionItem> BoostActions { get; } = new();
    public ObservableCollection<AssistedAction> AssistedActions { get; } = new();
    public ObservableCollection<string> Alerts { get; } = new();
    public ObservableCollection<double> CpuHistory24h { get; } = new();
    public ObservableCollection<double> CpuHistory7d { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> CpuTempHistory { get; } = new();
    public ObservableCollection<double> GpuTempHistory { get; } = new();
    public ObservableCollection<double> DiskReadHistory { get; } = new();
    public ObservableCollection<double> DiskWriteHistory { get; } = new();
    public ObservableCollection<TcpConnectionInfo> ActiveTcpConnections { get; } = new();
    public ObservableCollection<ProcessNetworkUsage> TopNetworkProcesses { get; } = new();
    public ObservableCollection<GameServerLatency> GameServerLatencies { get; } = new();
    public ObservableCollection<double> RiotPingHistory { get; } = new();
    public ObservableCollection<double> ValvePingHistory { get; } = new();
    public ObservableCollection<double> EaPingHistory { get; } = new();
    public ObservableCollection<double> EpicPingHistory { get; } = new();
    public ISeries[] Cpu24Series { get; }
    public ISeries[] Cpu7Series { get; }
    public ISeries[] GpuSeries { get; }
    public ISeries[] TempSeries { get; }
    public ISeries[] DiskIoSeries { get; }
    public ISeries[] GamePingSeries { get; }
    public Axis[] CpuYAxes { get; }
    public Axis[] GpuYAxes { get; }
    public Axis[] TempYAxes { get; }
    public Axis[] DiskYAxes { get; }
    public Axis[] PingYAxes { get; }
    public Axis[] Cpu24XAxes { get; }
    public Axis[] Cpu7XAxes { get; }

    public void SetOptimizationState(bool isRunning, int progress, string stageText)
    {
        IsOptimizing = isRunning;
        OptimizationProgress = Math.Clamp(progress, 0, 100);
        OptimizationStageText = string.IsNullOrWhiteSpace(stageText) ? "Optimisation" : stageText;
        OnPropertyChanged(nameof(OptimizationProgressLabel));
    }

    public void Update(MonitoringFrame frame)
    {
        CpuPercent = frame.Snapshot.CpuPercent;
        MemoryUsedMb = frame.Snapshot.MemoryUsedMb;
        MemoryTotalMb = frame.Snapshot.MemoryTotalMb;
        CpuTempLabel = frame.Snapshot.CpuTemperatureC is null ? "N/A" : $"{frame.Snapshot.CpuTemperatureC:F1} C";
        CpuTempPercent = frame.Snapshot.CpuTemperatureC is { } cpuTempC ? Math.Clamp(cpuTempC, 0, 100) : 0;
        GpuTempLabel = frame.Snapshot.GpuTemperatureC is null ? "N/A" : $"{frame.Snapshot.GpuTemperatureC:F1} C";
        GpuTempPercent = frame.Snapshot.GpuTemperatureC is { } gpuTempC ? Math.Clamp(gpuTempC, 0, 100) : 0;
        GpuUsagePercent = Math.Clamp(frame.Snapshot.GpuUsagePercent ?? 0, 0, 100);
        GpuUsageLabel = frame.Snapshot.GpuUsagePercent is null ? "N/A" : $"{frame.Snapshot.GpuUsagePercent:F0}%";
        FanLabel = frame.Snapshot.FanSpeedRpm is null ? "N/A" : $"{frame.Snapshot.FanSpeedRpm:F0} RPM";
        VramLabel = frame.Snapshot.VramTotalMb <= 0 ? "N/A"
            : $"{frame.Snapshot.VramUsedMb:F0} / {frame.Snapshot.VramTotalMb:F0} MB";
        VramTotalMb = frame.Snapshot.VramTotalMb;
        DiskIoLabel = $"R:{frame.Snapshot.DiskIoReadMbps:F1}  W:{frame.Snapshot.DiskIoWriteMbps:F1} MB/s";
        EstimatedFpsLabel = frame.Snapshot.EstimatedFps > 0 ? $"FPS: {frame.Snapshot.EstimatedFps:F0}" : "FPS: N/A";

        var net = frame.Snapshot.Network;
        NetworkLabel = $"↑{net.UploadKbps:F0} KB/s  ↓{net.DownloadKbps:F0} KB/s";
        ConnectionTypeLabel = frame.ConnectionKind switch
        {
            Infrastructure.Collectors.ActiveConnectionKind.Wireless => "Connexion : Wi-Fi",
            Infrastructure.Collectors.ActiveConnectionKind.Wired => "Connexion : Ethernet",
            _ => "Connexion : inconnue"
        };
        PingLabel = net.PingMs < 0 ? "Ping: N/A" : $"Ping: {net.PingMs:F0} ms";

        HealthScore = frame.Analysis.HealthScore;
        RiskLevel = frame.Analysis.RiskLevel;

        MachineName = frame.Profile.MachineName;
        OperatingSystem = frame.Profile.OperatingSystem;
        Architecture = frame.Profile.Architecture;
        CpuModel = frame.Profile.CpuModel;
        GpuModel = frame.Profile.GpuModel;
        Motherboard = frame.Profile.Motherboard;
        BiosVersion = frame.Profile.BiosVersion;
        LogicalCores = frame.Profile.LogicalCores;
        InstalledRamGb = frame.Profile.TotalRamGb;
        TotalDiskGb = frame.Profile.TotalDiskGb;
        UptimeLabel = $"Uptime: {frame.Profile.Uptime.Days}j {frame.Profile.Uptime.Hours}h {frame.Profile.Uptime.Minutes}m";

        LastUpdate = frame.Snapshot.Timestamp.ToString("HH:mm:ss");

        Replace(TopProcesses, frame.Snapshot.TopProcesses);
        Replace(Disks, frame.Profile.Disks);
        Replace(InstalledGames, frame.Profile.InstalledGames);
        Replace(PerCoreCpu, frame.Snapshot.PerCoreCpu);
        Replace(ActiveTcpConnections, frame.Snapshot.ActiveTcpConnections);
        Replace(TopNetworkProcesses, frame.Snapshot.TopNetworkProcesses);
        Replace(GameServerLatencies, frame.Snapshot.GameServerLatencies);
        Replace(Recommendations, frame.Analysis.Recommendations);
        Replace(BoostActions, frame.Analysis.BoostActions);
        Replace(AssistedActions, frame.Analysis.AssistedActions);
        Replace(Alerts, frame.Analysis.Alerts);
        NoAlerts = frame.Analysis.Alerts.Count == 0;

        // GPU usage history (last 60 ticks)
        AddCapped(GpuHistory, frame.Snapshot.GpuUsagePercent ?? 0, 60);
        ((LineSeries<double>)GpuSeries[0]).Values = GpuHistory.ToArray();

        // Temperature histories (last 60 ticks)
        AddCapped(CpuTempHistory, frame.Snapshot.CpuTemperatureC ?? 0, 60);
        AddCapped(GpuTempHistory, frame.Snapshot.GpuTemperatureC ?? 0, 60);
        ((LineSeries<double>)TempSeries[0]).Values = CpuTempHistory.ToArray();
        ((LineSeries<double>)TempSeries[1]).Values = GpuTempHistory.ToArray();

        if (ShouldSampleDiskHistory(frame.Snapshot.Timestamp))
        {
            AddCapped(DiskReadHistory, frame.Snapshot.DiskIoReadMbps, 1440);
            AddCapped(DiskWriteHistory, frame.Snapshot.DiskIoWriteMbps, 1440);
            ((LineSeries<double>)DiskIoSeries[0]).Values = DiskReadHistory.ToArray();
            ((LineSeries<double>)DiskIoSeries[1]).Values = DiskWriteHistory.ToArray();
        }

        UpdateGamePingHistory(frame.Snapshot.GameServerLatencies);

        OnPropertyChanged(nameof(GpuSeries));
        OnPropertyChanged(nameof(TempSeries));
        OnPropertyChanged(nameof(DiskIoSeries));
        OnPropertyChanged(nameof(GamePingSeries));

        OnPropertyChanged(nameof(MemoryPercent));
        OnPropertyChanged(nameof(CpuPercentLabel));
        OnPropertyChanged(nameof(MemoryPercentLabel));
        OnPropertyChanged(nameof(MemoryDetailLabel));
        OnPropertyChanged(nameof(HealthScoreLabel));
        OnPropertyChanged(nameof(RiskLevelLabel));
        OnPropertyChanged(nameof(LastUpdateLabel));
        OnPropertyChanged(nameof(HealthScoreLongLabel));
        OnPropertyChanged(nameof(HealthRiskLongLabel));
        OnPropertyChanged(nameof(MachineLabel));
        OnPropertyChanged(nameof(OsLabel));
        OnPropertyChanged(nameof(ArchitectureLabel));
        OnPropertyChanged(nameof(CpuLabel));
        OnPropertyChanged(nameof(GpuLabel));
        OnPropertyChanged(nameof(MotherboardLabel));
        OnPropertyChanged(nameof(BiosLabel));
        OnPropertyChanged(nameof(InstalledRamLabel));
        OnPropertyChanged(nameof(TotalDiskLabel));
        OnPropertyChanged(nameof(GamesCountLabel));
    }

    /// <summary>
    /// Alimente les graphes 24 h / 7 j depuis l'historique persistant SQLite
    /// (moyennes horaires et journalières réelles, pas une fenêtre glissante
    /// de ticks).
    /// </summary>
    public void UpdatePersistedCpuHistory(IReadOnlyList<double> hourlyAverages24h, IReadOnlyList<double> dailyAverages7d)
    {
        Replace(CpuHistory24h, hourlyAverages24h);
        Replace(CpuHistory7d, dailyAverages7d);

        ((LineSeries<double>)Cpu24Series[0]).Values = CpuHistory24h.ToArray();
        ((LineSeries<double>)Cpu7Series[0]).Values = CpuHistory7d.ToArray();

        OnPropertyChanged(nameof(Cpu24Series));
        OnPropertyChanged(nameof(Cpu7Series));
    }

    public void UpdateActiveGame(ActiveGameSession? session)
    {
        if (session is null)
        {
            ActiveGameLabel = "Aucun jeu detecte";
            ActiveGameServerLabel = "Serveur: N/A";
            IsGameActive = false;
        }
        else
        {
            var fps = session.EstimatedFps > 0 ? $"FPS: {session.EstimatedFps:F0}" : "FPS: N/A";
            var ping = session.ServerPingMs >= 0 ? $"{session.ServerPingMs:F0} ms" : "N/A";
            ActiveGameLabel = $"{session.GameName}  |  {session.SessionDuration.Hours}h{session.SessionDuration.Minutes:D2}m  |  RAM: {session.MemoryMb:F0} MB  |  {fps}";
            ActiveGameServerLabel = session.ServerEndpoint is null
                ? "Serveur: N/A"
                : $"Serveur: {session.ServerEndpoint}  |  Ping: {ping}";
            IsGameActive = true;
        }
    }

    private bool ShouldSampleDiskHistory(DateTime timestamp)
    {
        if (DiskReadHistory.Count == 0 || DiskWriteHistory.Count == 0)
        {
            _lastDiskHistorySample = timestamp;
            return true;
        }

        if ((timestamp - _lastDiskHistorySample).TotalMinutes < 1)
            return false;

        _lastDiskHistorySample = timestamp;
        return true;
    }

    private void UpdateGamePingHistory(IReadOnlyList<GameServerLatency> latencies)
    {
        AddProviderPing(RiotPingHistory, latencies, "Riot");
        AddProviderPing(ValvePingHistory, latencies, "Valve");
        AddProviderPing(EaPingHistory, latencies, "EA");
        AddProviderPing(EpicPingHistory, latencies, "Epic");

        ((LineSeries<double>)GamePingSeries[0]).Values = RiotPingHistory.ToArray();
        ((LineSeries<double>)GamePingSeries[1]).Values = ValvePingHistory.ToArray();
        ((LineSeries<double>)GamePingSeries[2]).Values = EaPingHistory.ToArray();
        ((LineSeries<double>)GamePingSeries[3]).Values = EpicPingHistory.ToArray();
    }

    private static void AddProviderPing(ObservableCollection<double> history, IReadOnlyList<GameServerLatency> latencies, string provider)
    {
        var value = latencies.FirstOrDefault(x => x.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase))?.PingMs ?? 0;
        AddCapped(history, Math.Max(0, value), 360);
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    private static void AddCapped(ObservableCollection<double> collection, double value, int max)
    {
        collection.Add(value);
        while (collection.Count > max)
            collection.RemoveAt(0);
    }

}
