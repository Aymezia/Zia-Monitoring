using System.Globalization;
using System.Net;
using System.Text;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Expose les métriques courantes au format Prometheus sur
/// http://localhost:{port}/metrics, pour brancher un Grafana local ou tout
/// scraper compatible. Lié à "localhost" uniquement (pas "+") : non
/// accessible depuis le réseau par défaut, choix de sécurité délibéré.
/// </summary>
public sealed class PrometheusExporterService : IDisposable
{
    private readonly object _gate = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private MonitoringFrame? _latestFrame;

    public bool IsRunning => _listener?.IsListening == true;
    public int Port { get; private set; }

    /// <summary>Met à jour les données servies. Appelé chaque cycle de monitoring.</summary>
    public void UpdateFrame(MonitoringFrame frame)
    {
        lock (_gate) { _latestFrame = frame; }
    }

    public (bool Success, string Message) Start(int port = 9877)
    {
        if (IsRunning)
            return (true, $"Déjà actif : http://localhost:{Port}/metrics");

        try
        {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();

            _listener = listener;
            Port = port;
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ListenLoop(listener, _cts.Token));

            Infrastructure.AppLog.Info($"Export Prometheus démarré sur http://localhost:{port}/metrics");
            return (true, $"Actif : http://localhost:{port}/metrics");
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Démarrage de l'export Prometheus impossible", ex);
            return (false, $"Impossible de démarrer (port {port} déjà utilisé ?) : {ex.Message}");
        }
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }
        finally
        {
            _listener = null;
        }
    }

    private async Task ListenLoop(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                break; // listener arrêté (Stop() a été appelé).
            }

            try
            {
                HandleRequest(context);
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn("Requête Prometheus en échec", ex);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        MonitoringFrame? frame;
        lock (_gate) { frame = _latestFrame; }

        var body = frame is null
            ? "# Aucune donnée disponible pour le moment\n"
            : BuildExposition(frame);

        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentType = "text/plain; version=0.0.4; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    /// <summary>Formatage pur (aucun I/O) — testable indépendamment du HttpListener.</summary>
    internal static string BuildExposition(MonitoringFrame frame)
    {
        var sb = new StringBuilder();
        var s = frame.Snapshot;

        AppendMetric(sb, "zia_cpu_percent", "Utilisation CPU (%)", s.CpuPercent);
        AppendMetric(sb, "zia_memory_used_mb", "Mémoire utilisée (Mo)", s.MemoryUsedMb);
        AppendMetric(sb, "zia_memory_total_mb", "Mémoire totale (Mo)", s.MemoryTotalMb);
        if (s.CpuTemperatureC is { } cpuTemp) AppendMetric(sb, "zia_cpu_temperature_celsius", "Température CPU", cpuTemp);
        if (s.GpuTemperatureC is { } gpuTemp) AppendMetric(sb, "zia_gpu_temperature_celsius", "Température GPU", gpuTemp);
        if (s.GpuUsagePercent is { } gpuUsage) AppendMetric(sb, "zia_gpu_usage_percent", "Utilisation GPU (%)", gpuUsage);
        AppendMetric(sb, "zia_disk_read_mbps", "Lecture disque (Mo/s)", s.DiskIoReadMbps);
        AppendMetric(sb, "zia_disk_write_mbps", "Écriture disque (Mo/s)", s.DiskIoWriteMbps);
        AppendMetric(sb, "zia_estimated_fps", "FPS estimés ou réels (PresentMon)", s.EstimatedFps);
        AppendMetric(sb, "zia_health_score", "Score de santé global (0-100)", frame.Analysis.HealthScore);
        AppendMetric(sb, "zia_network_upload_kbps", "Débit montant (Ko/s)", s.Network.UploadKbps);
        AppendMetric(sb, "zia_network_download_kbps", "Débit descendant (Ko/s)", s.Network.DownloadKbps);
        AppendMetric(sb, "zia_network_ping_ms", "Latence réseau (ms)", s.Network.PingMs);

        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, string name, string help, double value)
    {
        sb.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        sb.Append("# TYPE ").Append(name).AppendLine(" gauge");
        sb.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    public void Dispose() => Stop();
}
