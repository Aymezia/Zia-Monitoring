using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App;

public sealed partial class PerformanceOverlayWindow : Window
{
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly nint HwndTopmost = new(-1);

    private readonly nint _hwnd;
    private bool _visible;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    public PerformanceOverlayWindow()
    {
        InitializeComponent();
        Root.DataContext = ((App)Microsoft.UI.Xaml.Application.Current).State;
        _hwnd = WindowNative.GetWindowHandle(this);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(260, 230));
        AppWindow.Move(new Windows.Graphics.PointInt32(24, 80));
        AppWindow.IsShownInSwitchers = false;
        SetTopMost();
        HideOverlay();
    }

    private const int OverlayWidth = 260;
    private const int OverlayHeight = 230;

    public void ApplySettings(AppSettings settings)
    {
        Root.Opacity = Math.Clamp(settings.MiniWidgetOpacity, 0.35, 1.0);

        var displays = DisplayArea.FindAll();
        var display = settings.OverlayMonitorIndex >= 0 && settings.OverlayMonitorIndex < displays.Count
            ? displays[settings.OverlayMonitorIndex]
            : DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);

        var workArea = display.WorkArea;
        var monitorArea = new MonitorArea(workArea.X, workArea.Y, workArea.Width, workArea.Height);
        var point = OverlayPositionCalculator.ComputeCornerPosition(monitorArea, settings.OverlayPosition, OverlayWidth, OverlayHeight);

        AppWindow.Move(new Windows.Graphics.PointInt32(point.X, point.Y));
        SetTopMost();
    }

    /// <summary>Écrans disponibles pour le sélecteur de la page Paramètres.</summary>
    public static IReadOnlyList<string> GetMonitorNames()
    {
        var displays = DisplayArea.FindAll();
        var names = new List<string>();
        for (var i = 0; i < displays.Count; i++)
        {
            var area = displays[i].WorkArea;
            var primary = displays[i].DisplayId == DisplayArea.Primary.DisplayId ? " (principal)" : "";
            names.Add($"Écran {i + 1} — {area.Width}×{area.Height}{primary}");
        }
        return names;
    }

    public void ShowOverlay()
    {
        if (_visible)
        {
            SetTopMost();
            return;
        }

        ShowWindow(_hwnd, SwShowNoActivate);
        _visible = true;
        SetTopMost();
    }

    public void HideOverlay()
    {
        if (!_visible)
            return;

        ShowWindow(_hwnd, SwHide);
        _visible = false;
    }

    private void SetTopMost()
    {
        SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }
}
