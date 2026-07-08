using System.Diagnostics;

namespace ZiaMonitoring_App.Application;

public sealed record LeakSuspect(string ProcessName, int Pid, double StartMb, double CurrentMb, TimeSpan Window)
{
    public string Label =>
        $"{ProcessName} (PID {Pid}) : {StartMb:F0} → {CurrentMb:F0} Mo en {(int)Window.TotalMinutes} min, sans jamais redescendre.";
}

/// <summary>
/// Détection de fuite mémoire : échantillonne la RAM de chaque process toutes
/// les 5 minutes et signale ceux dont la consommation grimpe de façon quasi
/// monotone sur au moins une heure, avec une croissance significative.
/// Heuristique volontairement conservatrice : un cache qui se remplit peut
/// ressembler à une fuite, d'où le seuil élevé et l'exigence de monotonie.
/// </summary>
public sealed class MemoryLeakDetector
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);
    private const int MaxSamplesPerProcess = 36;        // 3 h de fenêtre glissante
    private const int MinSamplesForVerdict = 12;        // ≥ 1 h d'observation
    private const double MinGrowthMb = 300;             // croissance totale minimale
    private const double MaxDipRatio = 0.03;            // une baisse > 3 % entre 2 points disqualifie
    private const double MinRisingStepsRatio = 0.75;    // ≥ 75 % des pas doivent monter

    private readonly object _gate = new();
    private readonly Dictionary<int, (string Name, List<(DateTime At, double Mb)> Samples)> _history = new();
    private DateTime _lastCapture = DateTime.MinValue;
    private IReadOnlyList<LeakSuspect> _suspects = [];

    public IReadOnlyList<LeakSuspect> Suspects
    {
        get { lock (_gate) return _suspects; }
    }

    /// <summary>Appelé depuis la boucle de monitoring ; ne fait rien tant que l'intervalle n'est pas écoulé.</summary>
    public void MaybeCapture()
    {
        if (DateTime.UtcNow - _lastCapture < SampleInterval)
            return;
        _lastCapture = DateTime.UtcNow;

        var snapshot = new List<(int Pid, string Name, double Mb)>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    snapshot.Add((process.Id, process.ProcessName, process.WorkingSet64 / 1024.0 / 1024.0));
                }
                catch
                {
                    // Process terminé entre l'énumération et la lecture : attendu.
                }
            }
        }

        Observe(snapshot, DateTime.UtcNow);
    }

    /// <summary>Cœur testable : intègre un échantillon et met à jour la liste des suspects.</summary>
    internal void Observe(IReadOnlyList<(int Pid, string Name, double Mb)> snapshot, DateTime now)
    {
        lock (_gate)
        {
            var alive = snapshot.Select(s => s.Pid).ToHashSet();
            foreach (var deadPid in _history.Keys.Where(pid => !alive.Contains(pid)).ToList())
                _history.Remove(deadPid);

            foreach (var (pid, name, mb) in snapshot)
            {
                if (!_history.TryGetValue(pid, out var entry))
                    _history[pid] = entry = (name, []);

                entry.Samples.Add((now, mb));
                if (entry.Samples.Count > MaxSamplesPerProcess)
                    entry.Samples.RemoveAt(0);
            }

            _suspects = _history
                .Select(kvp => AnalyzeSamples(kvp.Key, kvp.Value.Name, kvp.Value.Samples))
                .Where(s => s is not null)
                .Cast<LeakSuspect>()
                .OrderByDescending(s => s.CurrentMb - s.StartMb)
                .ToList();
        }
    }

    internal static LeakSuspect? AnalyzeSamples(int pid, string name, IReadOnlyList<(DateTime At, double Mb)> samples)
    {
        if (samples.Count < MinSamplesForVerdict)
            return null;

        var start = samples[0];
        var current = samples[^1];
        if (current.Mb - start.Mb < MinGrowthMb)
            return null;

        var risingSteps = 0;
        for (var i = 1; i < samples.Count; i++)
        {
            var previous = samples[i - 1].Mb;
            var delta = samples[i].Mb - previous;

            if (delta < -previous * MaxDipRatio)
                return null; // vraie libération de mémoire : pas une fuite.

            if (delta > 0)
                risingSteps++;
        }

        if (risingSteps < (samples.Count - 1) * MinRisingStepsRatio)
            return null;

        return new LeakSuspect(name, pid, start.Mb, current.Mb, current.At - start.At);
    }
}
