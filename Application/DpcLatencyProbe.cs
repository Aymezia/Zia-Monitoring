using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ZiaMonitoring_App.Application;

public sealed record LatencyProbeResult(double MaxDeviationUs, double AvgDeviationUs)
{
    public string Verdict => Classify(MaxDeviationUs);
    public string Label => $"Écart max {MaxDeviationUs:F0} µs · moyen {AvgDeviationUs:F0} µs — {Verdict}";

    internal static string Classify(double maxUs) => maxUs switch
    {
        < 1000 => "Excellent : système très réactif, aucun risque de crépitement audio.",
        < 2000 => "Correct : réactivité satisfaisante pour le jeu et l'audio.",
        < 4000 => "Limite : des micro-freezes ou crépitements audio sont possibles sous charge.",
        _ => "Problématique : latence élevée, probable cause de crépitements/stutters. Suspectez un pilote (réseau, GPU, audio, chipset)."
    };
}

/// <summary>
/// Mesure la réactivité temps réel du système en observant l'écart entre des
/// attentes de 1 ms demandées et leur durée réelle : quand un pilote monopolise
/// le processeur (DPC/ISR trop longs), ces attentes dérapent, ce qui provoque
/// crépitements audio et micro-freezes. C'est une sonde de réactivité, pas un
/// traceur ETW complet : elle donne le chiffre-clé (comme LatencyMon) mais ne
/// peut pas nommer le pilote fautif — un outil ETW dédié reste nécessaire pour ça.
/// </summary>
public static class DpcLatencyProbe
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint uMilliseconds);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint uMilliseconds);

    public static Task<LatencyProbeResult> MeasureAsync(int durationMs = 3000, CancellationToken ct = default)
        => Task.Run(() => Measure(durationMs, ct), ct);

    private static LatencyProbeResult Measure(int durationMs, CancellationToken ct)
    {
        timeBeginPeriod(1);
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var deadline = TimeSpan.FromMilliseconds(durationMs);
            double maxDeviationUs = 0;
            double sumDeviationUs = 0;
            var samples = 0;

            var previous = stopwatch.Elapsed;
            while (stopwatch.Elapsed < deadline && !ct.IsCancellationRequested)
            {
                Thread.Sleep(1);
                var now = stopwatch.Elapsed;
                var actualUs = (now - previous).TotalMicroseconds;
                previous = now;

                // Écart au-delà du 1 ms demandé = temps pendant lequel le
                // système n'a pas pu nous redonner la main.
                var deviationUs = Math.Max(0, actualUs - 1000);
                maxDeviationUs = Math.Max(maxDeviationUs, deviationUs);
                sumDeviationUs += deviationUs;
                samples++;
            }

            var avg = samples > 0 ? sumDeviationUs / samples : 0;
            return new LatencyProbeResult(maxDeviationUs, avg);
        }
        finally
        {
            timeEndPeriod(1);
        }
    }
}
