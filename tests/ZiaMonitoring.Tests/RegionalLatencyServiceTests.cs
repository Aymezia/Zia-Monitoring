using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class RegionalLatencyServiceTests
{
    [Fact]
    public void RankResults_TrieParLatenceCroissante_MeilleureEnPremier()
    {
        var raw = new List<RegionalLatency>
        {
            new("EU", "eu.example.com", 80, true),
            new("US", "us.example.com", 25, true),
            new("Asie", "asia.example.com", 180, true)
        };

        var ranked = RegionalLatencyService.RankResults(raw);

        Assert.Equal("US", ranked[0].Region);
        Assert.True(ranked[0].IsBest);
        Assert.False(ranked[1].IsBest);
        Assert.False(ranked[2].IsBest);
    }

    [Fact]
    public void RankResults_RegionsInjoignables_PasseesEnDernier()
    {
        var raw = new List<RegionalLatency>
        {
            new("Injoignable", "down.example.com", -1, false),
            new("US", "us.example.com", 30, true)
        };

        var ranked = RegionalLatencyService.RankResults(raw);

        Assert.Equal("US", ranked[0].Region);
        Assert.True(ranked[0].IsBest);
        Assert.Equal("Injoignable", ranked[1].Region);
    }

    [Fact]
    public void RankResults_AucuneRegionJoignable_AucunMeilleur()
    {
        var raw = new List<RegionalLatency>
        {
            new("A", "a.example.com", -1, false),
            new("B", "b.example.com", -1, false)
        };

        var ranked = RegionalLatencyService.RankResults(raw);

        Assert.DoesNotContain(ranked, r => r.IsBest);
    }
}
