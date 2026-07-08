using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed record FpsDropEvent(DateTime OccurredAt, double FromFps, double ToFps, string? TopProcessLabel)
{
    public string Label => TopProcessLabel is null
        ? $"{OccurredAt:HH:mm:ss} — chute de {FromFps:F0} à {ToFps:F0} FPS"
        : $"{OccurredAt:HH:mm:ss} — chute de {FromFps:F0} à {ToFps:F0} FPS, pic possible : {TopProcessLabel}";
}

/// <summary>
/// Corrélation temps réel (pas d'historique permanent) : détecte une chute
/// relative de FPS d'un cycle à l'autre et note le process le plus gourmand
/// en CPU au même instant, comme piste probable. Garde les 20 derniers
/// événements en mémoire.
/// </summary>
public sealed class FpsDropCorrelator
{
    private const int MaxEvents = 20;
    private const double DropRatioThreshold = 0.30;
    private const double MinFpsBaseline = 20;

    private readonly object _gate = new();
    private readonly LinkedList<FpsDropEvent> _events = new();
    private double? _lastFps;

    public IReadOnlyList<FpsDropEvent> RecentEvents
    {
        get { lock (_gate) return _events.ToList(); }
    }

    public void Observe(double currentFps, IReadOnlyList<ProcessInfo> topProcesses)
    {
        var previous = _lastFps;
        _lastFps = currentFps;

        if (previous is not { } prevFps)
            return;

        if (!IsSignificantDrop(prevFps, currentFps))
            return;

        var topCpu = topProcesses.OrderByDescending(p => p.CpuPercent).FirstOrDefault();
        var label = topCpu is not null ? $"{topCpu.Name} ({topCpu.CpuPercent:F0}% CPU)" : null;
        Add(new FpsDropEvent(DateTime.Now, prevFps, currentFps, label));
    }

    /// <summary>Chute relative significative depuis une base pas déjà trop basse (évite le bruit à faible FPS).</summary>
    internal static bool IsSignificantDrop(double previousFps, double currentFps)
    {
        if (previousFps < MinFpsBaseline || currentFps >= previousFps)
            return false;

        return (previousFps - currentFps) / previousFps >= DropRatioThreshold;
    }

    private void Add(FpsDropEvent evt)
    {
        lock (_gate)
        {
            _events.AddFirst(evt);
            while (_events.Count > MaxEvents)
                _events.RemoveLast();
        }
    }
}
