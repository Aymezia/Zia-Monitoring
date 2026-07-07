using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using ZiaMonitoring_App.Infrastructure.Collectors;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class WifiAlertTests
{
    private static ActiveGameSession Session(string name = "valorant") =>
        new(1234, name, 0, 500, TimeSpan.FromMinutes(10), 0, null, -1);

    [Fact]
    public void ShouldAlert_JeuActifSurWifiToastsActives_RetourneVrai()
    {
        Assert.True(WifiAlertPolicy.ShouldAlert(Session(), ActiveConnectionKind.Wireless, toastsEnabled: true));
    }

    [Fact]
    public void ShouldAlert_ConnexionFilaire_RetourneFaux()
    {
        Assert.False(WifiAlertPolicy.ShouldAlert(Session(), ActiveConnectionKind.Wired, toastsEnabled: true));
    }

    [Fact]
    public void ShouldAlert_ConnexionInconnue_RetourneFaux()
    {
        Assert.False(WifiAlertPolicy.ShouldAlert(Session(), ActiveConnectionKind.Unknown, toastsEnabled: true));
    }

    [Fact]
    public void ShouldAlert_AucunJeuActif_RetourneFaux()
    {
        Assert.False(WifiAlertPolicy.ShouldAlert(null, ActiveConnectionKind.Wireless, toastsEnabled: true));
    }

    [Fact]
    public void ShouldAlert_ToastsDesactives_RetourneFaux()
    {
        Assert.False(WifiAlertPolicy.ShouldAlert(Session(), ActiveConnectionKind.Wireless, toastsEnabled: false));
    }
}
