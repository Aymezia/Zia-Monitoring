using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class GamerTroubleshootingServiceTests
{
    [Theory]
    [InlineData("Fortnite", "EAC")]
    [InlineData("CS2", "VAC")]
    [InlineData("Apex Legends", "EAC")]
    [InlineData("Rainbow Six Siege", "BattlEye")]
    [InlineData("League of Legends", "patch")]
    [InlineData("DirectX", "DXGI_ERROR_DEVICE_REMOVED")]
    public void Search_NouveauxJeux_TrouveAuMoinsUneEntree(string product, string query)
    {
        var service = new GamerTroubleshootingService();

        var results = service.Search(product, query);

        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.Contains(product, r.Product, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_ProduitSansQuery_RetourneToutesLesEntreesDuProduit()
    {
        var service = new GamerTroubleshootingService();

        var results = service.Search("CS2", string.Empty);

        Assert.Equal(2, results.Count); // VAC + Shader Cache Stutter
    }

    [Fact]
    public void Diagnose_JeuAntiCheatSensible_ProduitDesSuggestionsAvecSources()
    {
        var service = new GamerTroubleshootingService();

        var report = service.Diagnose("Rainbow Six Siege", "BattlEye", autoCollect: null);

        Assert.NotEmpty(report.Suggestions);
        Assert.All(report.Suggestions, s => Assert.NotEmpty(s.Sources));
    }

    [Fact]
    public void Search_ProduitInconnu_RetourneListeVide()
    {
        var service = new GamerTroubleshootingService();

        Assert.Empty(service.Search("JeuInexistant", string.Empty));
    }
}
