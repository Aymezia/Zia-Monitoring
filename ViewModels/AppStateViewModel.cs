using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.ViewModels;

public sealed class AppStateViewModel : INotifyPropertyChanged
{
    private double _cpuPercent;
    private double _memoryUsedMb;
    private double _memoryTotalMb;
    private string _cpuTempLabel = "N/A";
    private string _gpuTempLabel = "N/A";
    private string _gpuUsageLabel = "N/A";
    private string _fanLabel = "N/A";
    private string _vramLabel = "N/A";
    private string _diskIoLabel = "N/A";
    private string _networkLabel = "-";
    private string _pingLabel = "-";
    private int _healthScore;
    private string _riskLevel = "Low";
    private string _machineName = string.Empty;
    private string _operatingSystem = string.Empty;
    private string _architecture = string.Empty;
    private string _cpuModel = string.Empty;
    private string _gpuModel = string.Empty;
    private string _motherboard = string.Empty;
    private string _biosVersion = string.Empty;
    private int _logicalCores;
    private double _installedRamGb;
    private double _totalDiskGb;
    private string _uptimeLabel = "-";
    private bool _isOptimizing;
    private int _optimizationProgress;
    private string _optimizationStageText = "Pret";
    private string _lastUpdate = "-";
    private string _activeGameLabel = "Aucun jeu detecte";
    private bool _isGameActive;

    public event PropertyChangedEventHandler? PropertyChanged;

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
    }

    public double CpuPercent { get => _cpuPercent; private set => SetField(ref _cpuPercent, value); }
    public double MemoryUsedMb { get => _memoryUsedMb; private set => SetField(ref _memoryUsedMb, value); }
    public double MemoryTotalMb { get => _memoryTotalMb; private set => SetField(ref _memoryTotalMb, value); }
    public string CpuTempLabel { get => _cpuTempLabel; private set => SetField(ref _cpuTempLabel, value); }
    public int HealthScore { get => _healthScore; private set => SetField(ref _healthScore, value); }
    public string RiskLevel { get => _riskLevel; private set => SetField(ref _riskLevel, value); }
    public string MachineName { get => _machineName; private set => SetField(ref _machineName, value); }
    public string OperatingSystem { get => _operatingSystem; private set => SetField(ref _operatingSystem, value); }
    public string Architecture { get => _architecture; private set => SetField(ref _architecture, value); }
    public string CpuModel { get => _cpuModel; private set => SetField(ref _cpuModel, value); }
    public string GpuModel { get => _gpuModel; private set => SetField(ref _gpuModel, value); }
    public string Motherboard { get => _motherboard; private set => SetField(ref _motherboard, value); }
    public string BiosVersion { get => _biosVersion; private set => SetField(ref _biosVersion, value); }
    public int LogicalCores { get => _logicalCores; private set => SetField(ref _logicalCores, value); }
    public double InstalledRamGb { get => _installedRamGb; private set => SetField(ref _installedRamGb, value); }
    public double TotalDiskGb { get => _totalDiskGb; private set => SetField(ref _totalDiskGb, value); }
    public string UptimeLabel { get => _uptimeLabel; private set => SetField(ref _uptimeLabel, value); }
    public bool IsOptimizing { get => _isOptimizing; private set => SetField(ref _isOptimizing, value); }
    public int OptimizationProgress { get => _optimizationProgress; private set => SetField(ref _optimizationProgress, value); }
    public string OptimizationStageText { get => _optimizationStageText; private set => SetField(ref _optimizationStageText, value); }
    public string LastUpdate { get => _lastUpdate; private set => SetField(ref _lastUpdate, value); }
    public string GpuTempLabel { get => _gpuTempLabel; private set => SetField(ref _gpuTempLabel, value); }
    public string GpuUsageLabel { get => _gpuUsageLabel; private set => SetField(ref _gpuUsageLabel, value); }
    public string FanLabel { get => _fanLabel; private set => SetField(ref _fanLabel, value); }
    public string VramLabel { get => _vramLabel; private set => SetField(ref _vramLabel, value); }
    public string DiskIoLabel { get => _diskIoLabel; private set => SetField(ref _diskIoLabel, value); }
    public string NetworkLabel { get => _networkLabel; private set => SetField(ref _networkLabel, value); }
    public string PingLabel { get => _pingLabel; private set => SetField(ref _pingLabel, value); }
    public string ActiveGameLabel { get => _activeGameLabel; private set => SetField(ref _activeGameLabel, value); }
    public bool IsGameActive { get => _isGameActive; private set => SetField(ref _isGameActive, value); }

    public double MemoryPercent => MemoryTotalMb <= 0 ? 0 : (MemoryUsedMb / MemoryTotalMb) * 100;
    public string CpuPercentLabel => $"{CpuPercent:F1}%";
    public string MemoryPercentLabel => $"{MemoryPercent:F1}%";
    public string HealthScoreLabel => $"{HealthScore}/100";
    public string RiskLevelLabel => $"Risk: {RiskLevel}";
    public string LastUpdateLabel => $"Derniere mise a jour: {LastUpdate}";
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
    public ISeries[] Cpu24Series { get; }
    public ISeries[] Cpu7Series { get; }
    public Axis[] CpuYAxes { get; }
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

        CpuTempLabel = frame.Snapshot.CpuTemperatureC is null ? "N/A" : $"{frame.Snapshot.CpuTemperatureC:F1} C";
        GpuTempLabel = frame.Snapshot.GpuTemperatureC is null ? "N/A" : $"{frame.Snapshot.GpuTemperatureC:F1} C";
        GpuUsageLabel = frame.Snapshot.GpuUsagePercent is null ? "N/A" : $"{frame.Snapshot.GpuUsagePercent:F0}%";
        FanLabel = frame.Snapshot.FanSpeedRpm is null ? "N/A" : $"{frame.Snapshot.FanSpeedRpm:F0} RPM";
        VramLabel = frame.Snapshot.VramTotalMb <= 0 ? "N/A"
            : $"{frame.Snapshot.VramUsedMb:F0} / {frame.Snapshot.VramTotalMb:F0} MB";
        DiskIoLabel = $"R:{frame.Snapshot.DiskIoReadMbps:F1}  W:{frame.Snapshot.DiskIoWriteMbps:F1} MB/s";

        var net = frame.Snapshot.Network;
        NetworkLabel = $"↑{net.UploadKbps:F0} KB/s  ↓{net.DownloadKbps:F0} KB/s";
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
        Replace(Recommendations, frame.Analysis.Recommendations);
        Replace(BoostActions, frame.Analysis.BoostActions);
        Replace(AssistedActions, frame.Analysis.AssistedActions);
        Replace(Alerts, frame.Analysis.Alerts);
        Replace(CpuHistory24h, frame.Analysis.CpuHistory24h);
        Replace(CpuHistory7d, frame.Analysis.CpuHistory7d);

        ((LineSeries<double>)Cpu24Series[0]).Values = CpuHistory24h.ToArray();
        ((LineSeries<double>)Cpu7Series[0]).Values = CpuHistory7d.ToArray();

        OnPropertyChanged(nameof(MemoryPercent));
        OnPropertyChanged(nameof(CpuPercentLabel));
        OnPropertyChanged(nameof(MemoryPercentLabel));
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
        OnPropertyChanged(nameof(Cpu24Series));
        OnPropertyChanged(nameof(Cpu7Series));
    }

    public void UpdateActiveGame(ActiveGameSession? session)
    {
        if (session is null)
        {
            ActiveGameLabel = "Aucun jeu detecte";
            IsGameActive = false;
        }
        else
        {
            ActiveGameLabel = $"{session.GameName}  |  {session.SessionDuration.Hours}h{session.SessionDuration.Minutes:D2}m  |  RAM: {session.MemoryMb:F0} MB";
            IsGameActive = true;
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
