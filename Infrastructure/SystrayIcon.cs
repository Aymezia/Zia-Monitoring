using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ZiaMonitoring_App.Infrastructure;

/// <summary>
/// Win32 system tray icon with tooltip showing live CPU/GPU stats.
/// </summary>
public sealed class SystrayIcon : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAYICON = WM_APP + 1;
    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;
    private const int WM_LBUTTONDBLCLK = 0x0203;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint hInstance, nint lpIconName);

    private NOTIFYICONDATA _iconData;
    private bool _added;
    private bool _disposed;
    private readonly nint _hwnd;

    public SystrayIcon(nint hwnd)
    {
        _hwnd = hwnd;
        _iconData = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = hwnd,
            uID = 1,
            uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
            uCallbackMessage = WM_TRAYICON,
            hIcon = LoadIcon(nint.Zero, new nint(32512)), // IDI_APPLICATION
            szTip = "Zia Monitoring"
        };

        try
        {
            Shell_NotifyIcon(NIM_ADD, ref _iconData);
            _added = true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Ajout de l'icone systray impossible", ex);
        }
    }

    public void UpdateTooltip(string text)
    {
        if (!_added || _disposed) return;
        try
        {
            _iconData.szTip = text.Length > 127 ? text[..127] : text;
            Shell_NotifyIcon(NIM_MODIFY, ref _iconData);
        }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added)
        {
            try { Shell_NotifyIcon(NIM_DELETE, ref _iconData); } catch { }
        }
    }
}
