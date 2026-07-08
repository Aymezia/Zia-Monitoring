using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class OverlayPositionCalculatorTests
{
    private static readonly MonitorArea PrimaryMonitor = new(0, 0, 1920, 1080);
    private static readonly MonitorArea SecondMonitor = new(1920, 0, 2560, 1440); // à droite du premier

    [Fact]
    public void ComputeCornerPosition_TopLeft_ColleAuBordSuperieurGauche()
    {
        var point = OverlayPositionCalculator.ComputeCornerPosition(PrimaryMonitor, OverlayCorner.TopLeft, 260, 230);

        Assert.Equal(24, point.X);
        Assert.Equal(80, point.Y);
    }

    [Fact]
    public void ComputeCornerPosition_TopRight_ColleAuBordDroit()
    {
        var point = OverlayPositionCalculator.ComputeCornerPosition(PrimaryMonitor, OverlayCorner.TopRight, 260, 230);

        Assert.Equal(1920 - 260 - 24, point.X);
        Assert.Equal(80, point.Y);
    }

    [Fact]
    public void ComputeCornerPosition_BottomRight_ColleAuCoinInferieurDroit()
    {
        var point = OverlayPositionCalculator.ComputeCornerPosition(PrimaryMonitor, OverlayCorner.BottomRight, 260, 230);

        Assert.Equal(1920 - 260 - 24, point.X);
        Assert.Equal(1080 - 230 - 24, point.Y);
    }

    [Fact]
    public void ComputeCornerPosition_EcranSecondaire_UtiliseSonDecalageX()
    {
        // Le second écran commence à X=1920 : les coordonnées doivent rester dans son propre référentiel.
        var point = OverlayPositionCalculator.ComputeCornerPosition(SecondMonitor, OverlayCorner.TopLeft, 260, 230);

        Assert.Equal(1920 + 24, point.X);
    }

    [Fact]
    public void ComputeCornerPosition_BottomLeft_ColleAuCoinInferieurGauche()
    {
        var point = OverlayPositionCalculator.ComputeCornerPosition(PrimaryMonitor, OverlayCorner.BottomLeft, 260, 230);

        Assert.Equal(24, point.X);
        Assert.Equal(1080 - 230 - 24, point.Y);
    }
}
