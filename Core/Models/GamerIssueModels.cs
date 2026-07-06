namespace ZiaMonitoring_App.Core.Models;

public sealed record IssueFixItem(
    string Product,
    string ErrorCode,
    string Symptom,
    IReadOnlyList<string> RootCauses,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<string> Sources);

public sealed record EventExcerpt(
    string Source,
    string Level,
    DateTime Time,
    string Message);

public sealed record AutoCollectReport(
    string Product,
    DateTime CollectedAt,
    string ObsLogPath,
    int ObsLinesScanned,
    string? DetectedGpuDriver,
    string? DetectedEncoder,
    IReadOnlyList<string> ObsLogFindings,
    IReadOnlyList<EventExcerpt> WindowsEvents,
    IReadOnlyList<string> CorrelationTags,
    IReadOnlyList<string> UsefulLinks);

public sealed record SuggestedFix(
    string Title,
    string Why,
    int ConfidenceScore,
    string ConfidenceLabel,
    string? SafeActionKey,
    bool IsSafeOneClick,
    IReadOnlyList<string> Sources);

public sealed record DiagnosisReport(
    string Product,
    string ErrorQuery,
    IReadOnlyList<string> Observations,
    IReadOnlyList<SuggestedFix> Suggestions,
    IReadOnlyList<string> Sources,
    AutoCollectReport? AutoCollect);

public sealed record SafeFixResult(
    bool Success,
    string ActionKey,
    string Message);
