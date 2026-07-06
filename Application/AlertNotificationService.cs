using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class AlertNotificationService
{
    private DateTime _lastCpuAlertTime = DateTime.MinValue;
    private DateTime _lastDiskAlertTime = DateTime.MinValue;
    private DateTime _lastTempAlertTime = DateTime.MinValue;
    private DateTime _lastDailySummaryDate = DateTime.MinValue.Date;

    private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(3);

    public void CheckAndNotify(MonitoringFrame frame, AppSettings settings)
    {
        if (!settings.EnableToastAlerts)
            return;

        CheckCpuAlert(frame.Snapshot, settings);
        CheckTemperatureAlert(frame.Snapshot, settings);
        CheckDiskAlert(frame.Profile, settings);
        CheckDailySummary(frame, settings);
    }

    private void CheckCpuAlert(SystemSnapshot snapshot, AppSettings settings)
    {
        if (snapshot.CpuPercent > settings.CpuAlertThresholdPercent && DateTime.Now - _lastCpuAlertTime > _alertCooldown)
        {
            _lastCpuAlertTime = DateTime.Now;
            SendToast("Zia Monitoring - CPU critique",
                $"Utilisation CPU: {snapshot.CpuPercent:F0}% (seuil: {settings.CpuAlertThresholdPercent:F0}%). Verifiez les processus actifs.");
        }
    }

    private void CheckTemperatureAlert(SystemSnapshot snapshot, AppSettings settings)
    {
        var cpuHot = snapshot.CpuTemperatureC >= settings.CpuTempAlertThresholdC;
        var gpuHot = snapshot.GpuTemperatureC >= settings.GpuTempAlertThresholdC;
        if ((cpuHot || gpuHot) && DateTime.Now - _lastTempAlertTime > _alertCooldown)
        {
            _lastTempAlertTime = DateTime.Now;
            var detail = cpuHot
                ? $"CPU a {snapshot.CpuTemperatureC:F0}°C (seuil: {settings.CpuTempAlertThresholdC:F0}°C)"
                : $"GPU a {snapshot.GpuTemperatureC:F0}°C (seuil: {settings.GpuTempAlertThresholdC:F0}°C)";
            SendToast("Zia Monitoring - Surchauffe", $"{detail}. Verifiez la ventilation.");
        }
    }

    private void CheckDiskAlert(PcProfile profile, AppSettings settings)
    {
        var criticalDisk = profile.Disks.FirstOrDefault(d => d.FreeGb < settings.DiskFreeAlertGb);
        if (criticalDisk is not null && DateTime.Now - _lastDiskAlertTime > _alertCooldown)
        {
            _lastDiskAlertTime = DateTime.Now;
            SendToast("Zia Monitoring - Disque plein",
                $"Disque {criticalDisk.Name}: seulement {criticalDisk.FreeGb:F1} GB libre (seuil: {settings.DiskFreeAlertGb:F0} GB).");
        }
    }

    private void CheckDailySummary(MonitoringFrame frame, AppSettings settings)
    {
        if (!settings.EnableDailyHealthSummary)
            return;

        var today = DateTime.Today;
        if (_lastDailySummaryDate == today)
            return;

        if (DateTime.Now.Hour >= 9)
        {
            _lastDailySummaryDate = today;
            SendToast("Zia Monitoring - Résumé sante du jour",
                $"Score: {frame.Analysis.HealthScore}/100 | Risque: {frame.Analysis.RiskLevel} | {frame.Analysis.Alerts.Count} alerte(s) active(s).");
        }
    }

    public static void SendToast(string title, string message)
    {
        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Toast may fail in unpackaged mode; not critical.
        }
    }
}
