using Microsoft.Win32;

namespace ZiaMonitoring_App.Infrastructure;

public static class AutoStartManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ZiaMonitoring";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is not null;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Lecture de la cle de demarrage automatique impossible", ex);
            return false;
        }
    }

    public static void Enable()
    {
        try
        {
            var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath)) return;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.SetValue(AppName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Activation du demarrage automatique impossible", ex);
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            AppLog.Warn("Desactivation du demarrage automatique impossible", ex);
        }
    }
}
