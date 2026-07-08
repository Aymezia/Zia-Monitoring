using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Application;

/// <summary>Zone de travail d'un écran, en coordonnées plan bureau (multi-écran).</summary>
public readonly record struct MonitorArea(int X, int Y, int Width, int Height);

public readonly record struct OverlayPoint(int X, int Y);

/// <summary>
/// Calcule la position de l'overlay pour un écran et un coin donnés. Types
/// plats (pas Windows.Graphics.PointInt32/RectInt32) pour rester testable
/// sans référence au Windows App SDK — la conversion se fait côté fenêtre.
/// </summary>
public static class OverlayPositionCalculator
{
    private const int Margin = 24;
    private const int TopOffset = 80; // dégage la barre de titre en position haute

    public static OverlayPoint ComputeCornerPosition(MonitorArea area, OverlayCorner corner, int overlayWidth, int overlayHeight)
    {
        return corner switch
        {
            OverlayCorner.TopLeft => new OverlayPoint(area.X + Margin, area.Y + TopOffset),
            OverlayCorner.TopRight => new OverlayPoint(area.X + area.Width - overlayWidth - Margin, area.Y + TopOffset),
            OverlayCorner.BottomLeft => new OverlayPoint(area.X + Margin, area.Y + area.Height - overlayHeight - Margin),
            OverlayCorner.BottomRight => new OverlayPoint(area.X + area.Width - overlayWidth - Margin, area.Y + area.Height - overlayHeight - Margin),
            _ => new OverlayPoint(area.X + Margin, area.Y + TopOffset)
        };
    }
}
