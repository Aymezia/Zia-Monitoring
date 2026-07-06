using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class AlertNotificationService
{
    private DateTime _lastCpuAlertTime = DateTime.MinValue;
    private DateTime _lastDiskAlertTime = DateTime.MinValue;
    private DateTime _lastDailySummaryDate = DateTime.MinValue.Date;

    private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(3);

    public void CheckAndNotify(MonitoringFrame frame, AppSettings settings)
    {
        if (!settings.EnableToastAlerts)
            return;

        CheckCpuAlert(frame.Snapshot, settings);
        CheckDiskAlert(frame.Profile, settings);
        CheckDailySummary(frame, settings);
    }

    private void CheckCpuAlert(SystemSnapshot snapshot, AppSettings settings)
    {
        if (snapshot.CpuPercent > 90 && DateTime.Now - _lastCpuAlertTime > _alertCooldown)
        {
            _lastCpuAlertTime = DateTime.Now;
            SendToast("Zia Monitoring - CPU critique",
                $"Utilisation CPU: {snapshot.CpuPercent:F0}%. Verifiez les processus actifs.");
        }
    }

    private void CheckDiskAlert(PcProfile profile, AppSettings settings)
    {
        var criticalDisk = profile.Disks.FirstOrDefault(d => d.FreeGb < 10);
        if (criticalDisk is not null && DateTime.Now - _lastDiskAlertTime > _alertCooldown)
        {
            _lastDiskAlertTime = DateTime.Now;
            SendToast("Zia Monitoring - Disque plein",
                $"Disque {criticalDisk.Name}: seulement {criticalDisk.FreeGb:F1} GB libre.");
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

    private static void SendToast(string title, string message)
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
