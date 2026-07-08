using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class DiskMaintenanceServiceTests
{
    [Fact]
    public void DiskForecast_CalculeLeNombreDeJeuxRestants()
    {
        var forecast = new DiskForecast("C:\\", FreeGb: 250, AverageGameSizeGb: 90);

        Assert.Equal(2, forecast.EstimatedGamesRemaining);
        Assert.Contains("2 jeu(x)", forecast.Label);
    }

    [Fact]
    public void DiskForecast_EspaceInsuffisant_MessageAdapte()
    {
        var forecast = new DiskForecast("D:\\", FreeGb: 40, AverageGameSizeGb: 90);

        Assert.Equal(0, forecast.EstimatedGamesRemaining);
        Assert.Contains("plus assez de place", forecast.Label);
    }

    [Fact]
    public void DuplicateGroup_CalculeLEspaceGaspille()
    {
        var group = new DuplicateGroup(FileSize: 10 * 1024 * 1024, Paths: ["a.zip", "b.zip", "c.zip"]);

        Assert.Equal(20, group.WastedMb, precision: 3);
        Assert.Contains("3 copies", group.Label);
    }

    [Fact]
    public void PageFileInfo_AutomatiquementGere_MessageApprobateur()
    {
        var info = new PageFileInfo(AutomaticallyManaged: true, CurrentAllocatedMb: 0, RecommendedMinMb: 0, RecommendedMaxMb: 0);

        Assert.Contains("Géré automatiquement", info.Recommendation);
    }

    [Fact]
    public void PageFileInfo_GereManuellement_IndiqueLaTailleActuelleEtRecommandee()
    {
        var info = new PageFileInfo(AutomaticallyManaged: false, CurrentAllocatedMb: 2048, RecommendedMinMb: 16384, RecommendedMaxMb: 24576);

        Assert.Contains("2048", info.Recommendation);
        Assert.Contains("16384", info.Recommendation);
    }
}
