namespace ZiaMonitoring_App.Core.Models;

public sealed record ProcessInfo(int Pid, string Name, double CpuPercent, double MemoryMb)
{
    public string MemoryLabel => $"{MemoryMb:F1} MB";
}

public sealed record PerCoreCpuUsage(int CoreIndex, double Percent);

public sealed record NetworkStats(double UploadKbps, double DownloadKbps, double PingMs);

public sealed record SystemSnapshot(
    DateTime Timestamp,
    double CpuPercent,
    double MemoryUsedMb,
    double MemoryTotalMb,
    double? CpuTemperatureC,
    double? GpuTemperatureC,
    double? GpuUsagePercent,
    double? FanSpeedRpm,
    double VramUsedMb,
    double VramTotalMb,
    double DiskIoReadMbps,
    double DiskIoWriteMbps,
    IReadOnlyList<PerCoreCpuUsage> PerCoreCpu,
    NetworkStats Network,
    IReadOnlyList<ProcessInfo> TopProcesses);

public sealed record DiskProfile(string Name, string Format, double TotalGb, double FreeGb)
{
    public string FreeLabel => $"Libre: {FreeGb:F1} GB / {TotalGb:F1} GB";
}

public sealed record GameInstallation(
    string Name,
    string Platform,
    string Version,
    string InstallLocation,
    TimeSpan PlayTime,
    string? LaunchUri)
{
    public string PlayTimeLabel => PlayTime.TotalMinutes > 0
        ? $"{(int)PlayTime.TotalHours}h{PlayTime.Minutes:D2}m"
        : "N/A";
}

public sealed record ActiveGameSession(string GameName, double CpuPercent, double MemoryMb, TimeSpan SessionDuration);

public sealed record BoostHistoryEntry(
    DateTime AppliedAt,
    int HealthScoreBefore,
    int HealthScoreAfter,
    IReadOnlyList<string> Actions);

public sealed record AppSettings(
    int RefreshIntervalSeconds,
    bool EnableToastAlerts,
    bool EnableDailyHealthSummary,
    bool AutoSilentModeOnGame,
    bool EnableCleanupScheduler,
    TimeSpan ScheduledCleanupTime,
    string Theme,
    bool ShowSystray,
    bool EnableOnboarding);

public sealed record OptimizationProfile(string Name, string Description, IReadOnlyList<string> Actions);

public sealed record SecurityReport(
    bool FirewallEnabled,
    bool UacEnabled,
    IReadOnlyList<string> OpenPorts,
    IReadOnlyList<string> SuspiciousStartupEntries,
    IReadOnlyList<string> ObsoleteDrivers,
    IReadOnlyList<string> DiskSmartWarnings);

public sealed record GameCompatScore(string GameName, int Score, string Verdict, IReadOnlyList<string> Issues);


public sealed record PcProfile(
    string MachineName,
    string OperatingSystem,
    string Architecture,
    string CpuModel,
    string GpuModel,
    string Motherboard,
    string BiosVersion,
    int LogicalCores,
    double TotalRamGb,
    double TotalDiskGb,
    TimeSpan Uptime,
    IReadOnlyList<DiskProfile> Disks,
    IReadOnlyList<GameInstallation> InstalledGames);

public sealed record Recommendation(string Priority, string Title, string Why, string SuggestedUpgrade)
{
    public string WhyLabel => $"Pourquoi: {Why}";
    public string SuggestionLabel => $"Suggestion: {SuggestedUpgrade}";
}

public sealed record AssistedAction(string Title, string Description, string ServiceLevel)
{
    public string ServiceLevelLabel => $"Niveau: {ServiceLevel}";
}

public sealed record BoostActionItem(string Name, string Description, string RiskLevel, bool RequiresRestart)
{
    public string RiskLabel => $"Risque: {RiskLevel}";
    public string RestartLabel => $"Redemarrage requis: {RequiresRestart}";
}

public sealed record AnalysisReport(
    int HealthScore,
    string RiskLevel,
    IReadOnlyList<Recommendation> Recommendations,
    IReadOnlyList<AssistedAction> AssistedActions,
    IReadOnlyList<string> Alerts,
    IReadOnlyList<double> CpuHistory24h,
    IReadOnlyList<double> CpuHistory7d,
    IReadOnlyList<BoostActionItem> BoostActions);

public sealed record MonitoringFrame(SystemSnapshot Snapshot, PcProfile Profile, AnalysisReport Analysis);
