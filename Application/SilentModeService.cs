using Microsoft.Win32;
using System.Diagnostics;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

public sealed class SilentModeService
{
    private bool _silentModeActive;

    private static readonly string[] SuspendCandidates =
    [
        "OneDrive", "Teams", "Slack", "Discord",
        "Spotify", "EpicGamesLauncher", "steamwebhelper"
    ];

    public bool IsActive => _silentModeActive;

    public void Activate(IList<string> warnings)
    {
        if (_silentModeActive)
            return;

        try
        {
            SetWindowsAnimations(false);
        }
        catch (Exception ex)
        {
            warnings.Add($"Animation registry: {ex.Message}");
        }

        _silentModeActive = true;
    }

    public void Deactivate(IList<string> warnings)
    {
        if (!_silentModeActive)
            return;

        try
        {
            SetWindowsAnimations(true);
        }
        catch (Exception ex)
        {
            warnings.Add($"Restore animation registry: {ex.Message}");
        }

        _silentModeActive = false;
    }

    public static void SetWindowsAnimations(bool enabled)
    {
        // SystemResponsiveness: 0 = prioritize games/apps, 20 = balanced
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            writable: true);
        key?.SetValue("SystemResponsiveness", enabled ? 20 : 0, RegistryValueKind.DWord);

        // Visual animation flags
        using var animKey = Registry.CurrentUser.OpenSubKey(
            @"Control Panel\Desktop",
            writable: true);
        animKey?.SetValue("UserPreferencesMask",
            enabled
                ? new byte[] { 0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 }
                : new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 },
            RegistryValueKind.Binary);
    }
}
