using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// FPS réels via PresentMon (Intel, capture ETW des Present DXGI/D3D) —
/// la méthode des overlays de référence (MSI Afterburner, PresentMon).
/// L'exécutable n'est pas embarqué : il est téléchargé à la demande depuis
/// les releases GitHub officielles GameTechDev/PresentMon.
/// Nécessite les droits administrateur (l'app les a déjà pour
/// LibreHardwareMonitor).
/// </summary>
public sealed class PresentMonService : IDisposable
{
    private const string LatestReleaseApi = "https://api.github.com/repos/GameTechDev/PresentMon/releases/latest";
    private static readonly string[] FrametimeColumnNames = ["FrameTime", "msBetweenPresents", "MsBetweenPresents"];

    private readonly string _toolPath;
    private readonly object _gate = new();
    private readonly Queue<double> _recentFrametimesMs = new();
    private Process? _process;
    private int _trackedPid;

    public PresentMonService(string? toolsDirectory = null)
    {
        var dir = toolsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZiaMonitoring", "tools");
        Directory.CreateDirectory(dir);
        _toolPath = Path.Combine(dir, "PresentMon.exe");
    }

    public bool IsAvailable => File.Exists(_toolPath);

    public bool IsRunning
    {
        get { lock (_gate) return _process is { HasExited: false }; }
    }

    /// <summary>FPS courant lissé (moyenne des ~120 dernières frames), null si inactif.</summary>
    public double? CurrentFps
    {
        get
        {
            lock (_gate)
            {
                if (_process is not { HasExited: false } || _recentFrametimesMs.Count < 10)
                    return null;

                var avgMs = _recentFrametimesMs.Average();
                return avgMs > 0 ? Math.Round(1000.0 / avgMs, 1) : null;
            }
        }
    }

    /// <summary>
    /// Démarre ou arrête la capture selon le jeu actif. À appeler chaque cycle
    /// de monitoring ; sans PresentMon installé, ne fait rien.
    /// </summary>
    public void EnsureTracking(int? gamePid)
    {
        if (!IsAvailable)
            return;

        lock (_gate)
        {
            var running = _process is { HasExited: false };

            if (gamePid is null || (running && _trackedPid != gamePid))
            {
                StopLocked();
                running = false;
            }

            if (gamePid is { } pid && !running)
                StartLocked(pid);
        }
    }

    /// <summary>Télécharge la dernière release console x64 de PresentMon.</summary>
    public async Task<(bool Success, string Message)> DownloadAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ZiaMonitoring", "1.0"));

            var json = await http.GetStringAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            string? assetUrl = null;
            string? assetName = null;
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                // L'app console s'appelle "PresentMon-x.y.z-x64.exe".
                if (name.StartsWith("PresentMon-", StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                    break;
                }
            }

            if (assetUrl is null)
                return (false, "Aucun exécutable x64 trouvé dans la dernière release PresentMon.");

            var bytes = await http.GetByteArrayAsync(assetUrl, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(_toolPath, bytes, ct).ConfigureAwait(false);

            Infrastructure.AppLog.Info($"PresentMon installé: {assetName} ({bytes.Length / 1024} Ko)");
            return (true, $"{assetName} installé. Les FPS réels seront capturés à la prochaine partie.");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Téléchargement de PresentMon impossible", ex);
            return (false, $"Téléchargement impossible : {ex.Message}");
        }
    }

    private void StartLocked(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo(_toolPath,
                $"--process_id {pid} --output_stdout --stop_existing_session --terminate_on_proc_exit")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi);
            if (process is null)
                return;

            _process = process;
            _trackedPid = pid;
            _recentFrametimesMs.Clear();

            _ = Task.Run(() => PumpOutput(process));
            Infrastructure.AppLog.Info($"PresentMon démarré sur le PID {pid}");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Démarrage de PresentMon impossible", ex);
            _process = null;
        }
    }

    private void StopLocked()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }
        catch { }

        _process = null;
        _recentFrametimesMs.Clear();
    }

    private void PumpOutput(Process process)
    {
        try
        {
            var header = process.StandardOutput.ReadLine();
            var frametimeIndex = FindFrametimeColumn(header);
            if (frametimeIndex < 0)
            {
                Infrastructure.AppLog.Warn($"PresentMon: colonne frametime introuvable dans '{header}'");
                return;
            }

            while (process.StandardOutput.ReadLine() is { } line)
            {
                var value = ParseFrametimeMs(line, frametimeIndex);
                if (value is not { } ms || ms <= 0 || ms > 1000)
                    continue;

                lock (_gate)
                {
                    _recentFrametimesMs.Enqueue(ms);
                    while (_recentFrametimesMs.Count > 120)
                        _recentFrametimesMs.Dequeue();
                }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du flux PresentMon interrompue", ex);
        }
    }

    internal static int FindFrametimeColumn(string? csvHeader)
    {
        if (string.IsNullOrWhiteSpace(csvHeader))
            return -1;

        var columns = csvHeader.Split(',');
        foreach (var name in FrametimeColumnNames)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                if (columns[i].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    internal static double? ParseFrametimeMs(string csvLine, int columnIndex)
    {
        var parts = csvLine.Split(',');
        if (columnIndex >= parts.Length)
            return null;

        return double.TryParse(parts[columnIndex],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopLocked();
        }
    }
}
