using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Détecte le throttling thermique CPU/GPU : température proche de la limite,
/// charge élevée, mais fréquence nettement sous le maximum observé. Utilise
/// une hystérésis (10 cycles consécutifs) pour éviter les faux positifs sur
/// les variations normales de boost.
/// </summary>
public sealed class ThrottlingDetector
{
    private const int ConfirmationCycles = 10;
    private const double ClockDropRatio = 0.82;
    private static readonly TimeSpan ToastCooldown = TimeSpan.FromMinutes(15);

    private double _maxObservedCpuClock;
    private double _maxObservedGpuClock;
    private int _cpuHits;
    private int _gpuHits;
    private DateTime _lastToast = DateTime.MinValue;

    /// <summary>Alerte active (affichée dans le panneau Alertes), sinon null.</summary>
    public string? ActiveAlert { get; private set; }

    /// <summary>
    /// À appeler chaque cycle. Retourne un message de toast quand un
    /// throttling vient d'être confirmé (une fois par période de cooldown).
    /// </summary>
    public string? Update(double? cpuTempC, double? cpuClockMhz, double cpuLoadPercent,
                          double? gpuTempC, double? gpuClockMhz, double? gpuLoadPercent)
    {
        // Les fréquences max observées servent de référence (boost réel de CETTE machine).
        if (cpuClockMhz is { } cpuClock && cpuLoadPercent >= 30)
            _maxObservedCpuClock = Math.Max(_maxObservedCpuClock, cpuClock);
        if (gpuClockMhz is { } gpuClock && gpuLoadPercent >= 30)
            _maxObservedGpuClock = Math.Max(_maxObservedGpuClock, gpuClock);

        var cpuThrottling = IsThrottling(
            cpuTempC, cpuClockMhz, cpuLoadPercent, _maxObservedCpuClock,
            tempThreshold: 92, ref _cpuHits);

        var gpuThrottling = IsThrottling(
            gpuTempC, gpuClockMhz, gpuLoadPercent ?? 0, _maxObservedGpuClock,
            tempThreshold: 87, ref _gpuHits);

        ActiveAlert = (cpuThrottling, gpuThrottling) switch
        {
            (true, true) => "Throttling thermique CPU et GPU : les fréquences chutent, vos performances sont bridées.",
            (true, false) => $"Throttling thermique CPU : {cpuClockMhz:F0} MHz sous charge (max observé {_maxObservedCpuClock:F0} MHz).",
            (false, true) => $"Throttling thermique GPU : {gpuClockMhz:F0} MHz sous charge (max observé {_maxObservedGpuClock:F0} MHz).",
            _ => null
        };

        if (ActiveAlert is not null && DateTime.Now - _lastToast > ToastCooldown)
        {
            _lastToast = DateTime.Now;
            return ActiveAlert + " Vérifiez la ventilation et la pâte thermique.";
        }

        return null;
    }

    private static bool IsThrottling(double? tempC, double? clockMhz, double loadPercent,
                                     double maxObservedClock, double tempThreshold, ref int hits)
    {
        var candidate = tempC >= tempThreshold
                        && loadPercent >= 70
                        && clockMhz is { } clock
                        && maxObservedClock > 500
                        && clock < maxObservedClock * ClockDropRatio;

        if (candidate)
        {
            hits = Math.Min(hits + 1, ConfirmationCycles);
        }
        else if (tempC < tempThreshold - 8 || loadPercent < 50)
        {
            hits = 0; // récupération franche : on réarme.
        }

        return hits >= ConfirmationCycles;
    }
}
