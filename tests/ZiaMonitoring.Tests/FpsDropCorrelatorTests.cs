using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class FpsDropCorrelatorTests
{
    [Theory]
    [InlineData(100, 60)]
    [InlineData(60, 30)]
    [InlineData(144, 90)]
    public void IsSignificantDrop_ChuteDAuMoins30Pourcent_RetourneTrue(double previous, double current)
    {
        Assert.True(FpsDropCorrelator.IsSignificantDrop(previous, current));
    }

    [Theory]
    [InlineData(100, 90)]
    [InlineData(60, 55)]
    public void IsSignificantDrop_ChuteLegere_RetourneFalse(double previous, double current)
    {
        Assert.False(FpsDropCorrelator.IsSignificantDrop(previous, current));
    }

    [Fact]
    public void IsSignificantDrop_FpsQuiRemonte_RetourneFalse()
    {
        Assert.False(FpsDropCorrelator.IsSignificantDrop(30, 60));
    }

    [Fact]
    public void IsSignificantDrop_BaseDejaTropBasse_IgnoreLeBruit()
    {
        // Base sous le seuil de bruit (MinFpsBaseline) : une chute de 15->5 ne doit pas alerter.
        Assert.False(FpsDropCorrelator.IsSignificantDrop(15, 5));
    }

    [Fact]
    public void Observe_ChuteSignificative_AjouteUnEvenementAvecLeProcessLePlusGourmand()
    {
        var correlator = new FpsDropCorrelator();
        var processes = new[]
        {
            new ProcessInfo(1, "chrome", 12, 200),
            new ProcessInfo(2, "game", 85, 4000)
        };

        correlator.Observe(120, processes);
        correlator.Observe(60, processes);

        var events = correlator.RecentEvents;
        Assert.Single(events);
        Assert.Contains("game", events[0].TopProcessLabel);
    }

    [Fact]
    public void Observe_PremiereMesure_NeDeclencheRien()
    {
        var correlator = new FpsDropCorrelator();
        correlator.Observe(30, Array.Empty<ProcessInfo>());

        Assert.Empty(correlator.RecentEvents);
    }
}
