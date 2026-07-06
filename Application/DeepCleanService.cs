namespace ZiaMonitoring_App.Application;

public sealed record CleanTargetResult(string Target, int DeletedFiles, double FreedMb, string? Error = null)
{
    public string Label => Error is null
        ? $"{Target}: {DeletedFiles} fichier(s), {FreedMb:F0} Mo libérés"
        : $"{Target}: {Error}";
}

/// <summary>
/// Nettoyage profond : dossiers temporaires (utilisateur + Windows) et
/// shader caches GPU (NVIDIA/AMD/Intel/DirectX). Les fichiers verrouillés ou
/// récents (moins de 4 h) sont ignorés ; les caches de shaders Steam ne sont
/// pas touchés (leur recompilation pénalise les temps de chargement).
/// Sert aussi de moteur au nettoyage planifié quotidien.
/// </summary>
public sealed class DeepCleanService
{
    private DateTime _lastScheduledRunDate = DateTime.MinValue;

    /// <summary>Exécute le nettoyage complet et retourne le détail par cible.</summary>
    public IReadOnlyList<CleanTargetResult> Run()
    {
        var results = new List<CleanTargetResult>
        {
            CleanDirectory("Temp utilisateur", Path.GetTempPath(), minAgeHours: 4),
            CleanDirectory("Temp Windows", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), minAgeHours: 4)
        };

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var (label, relative) in ShaderCacheTargets())
        {
            var path = Path.Combine(localAppData, relative);
            if (Directory.Exists(path))
                results.Add(CleanDirectory(label, path, minAgeHours: 0));
        }

        var summary = results.Where(r => r.Error is null).ToList();
        Infrastructure.AppLog.Info(
            $"Nettoyage profond: {summary.Sum(r => r.DeletedFiles)} fichier(s), {summary.Sum(r => r.FreedMb):F0} Mo libérés");

        return results;
    }

    /// <summary>
    /// Vrai si le nettoyage planifié doit se déclencher maintenant : activé,
    /// heure passée, pas encore exécuté aujourd'hui.
    /// </summary>
    public bool IsScheduledRunDue(bool enabled, TimeSpan scheduledTime)
    {
        if (!enabled || _lastScheduledRunDate == DateTime.Today)
            return false;

        if (DateTime.Now.TimeOfDay < scheduledTime)
            return false;

        _lastScheduledRunDate = DateTime.Today;
        return true;
    }

    private static IEnumerable<(string Label, string RelativePath)> ShaderCacheTargets() =>
    [
        ("Shader cache NVIDIA (DX)", @"NVIDIA\DXCache"),
        ("Shader cache NVIDIA (GL)", @"NVIDIA\GLCache"),
        ("Shader cache NVIDIA (NV_Cache)", @"NVIDIA Corporation\NV_Cache"),
        ("Shader cache AMD (DX)", @"AMD\DxCache"),
        ("Shader cache AMD (DX9)", @"AMD\DxcCache"),
        ("Shader cache AMD (GL)", @"AMD\GLCache"),
        ("Shader cache Intel", @"Intel\ShaderCache"),
        ("Shader cache DirectX", @"D3DSCache")
    ];

    private static CleanTargetResult CleanDirectory(string label, string root, int minAgeHours)
    {
        if (!Directory.Exists(root))
            return new CleanTargetResult(label, 0, 0);

        var deleted = 0;
        double freedBytes = 0;
        var threshold = DateTime.UtcNow.AddHours(-minAgeHours);

        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(20_000))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (minAgeHours > 0 && info.LastWriteTimeUtc >= threshold)
                        continue;

                    var size = info.Length;
                    info.Delete();
                    freedBytes += size;
                    deleted++;
                }
                catch
                {
                    // Fichier verrouillé ou protégé : attendu, on continue.
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Nettoyage de {label} incomplet", ex);
            return new CleanTargetResult(label, deleted, Math.Round(freedBytes / 1024 / 1024, 1), ex.Message);
        }

        return new CleanTargetResult(label, deleted, Math.Round(freedBytes / 1024 / 1024, 1));
    }
}
