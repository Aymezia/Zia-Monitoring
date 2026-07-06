namespace ZiaMonitoring_App.Core.Models;

public sealed record BoostPreviewResult(
    DateTime GeneratedAt,
    int TempFileCandidates,
    long TempBytesCandidates,
    IReadOnlyList<string> StartupCandidates,
    IReadOnlyList<string> ServiceCandidates,
    IReadOnlyList<string> PlannedActions);

public sealed record BoostExecutionResult(
    bool Success,
    string RollbackId,
    IReadOnlyList<string> AppliedActions,
    IReadOnlyList<string> Warnings);

public sealed record BoostRollbackResult(
    bool Success,
    string RollbackId,
    IReadOnlyList<string> RestoredActions,
    IReadOnlyList<string> Warnings);

public sealed class BoostRollbackState
{
    public string RollbackId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> DisabledStartupEntries { get; set; } = new();
    public List<string> StoppedServices { get; set; } = new();
}
