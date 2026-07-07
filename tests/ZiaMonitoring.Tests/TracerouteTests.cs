using ZiaMonitoring_App.Infrastructure.Collectors;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class TracerouteTests
{
    [Fact]
    public void AddressLabel_AucuneReponse_AfficheUnAsterisque()
    {
        var hop = new TracerouteHop(3, null, null, false);

        Assert.Equal("* (aucune réponse)", hop.AddressLabel);
        Assert.Equal("-", hop.RttLabel);
    }

    [Fact]
    public void AddressLabel_SautIntermediaire_AfficheLAdresseEtLeRtt()
    {
        var hop = new TracerouteHop(2, "192.168.1.1", 12.3, false);

        Assert.Equal("192.168.1.1", hop.AddressLabel);
        Assert.Equal("12 ms", hop.RttLabel);
        Assert.False(hop.IsDestination);
    }

    [Fact]
    public void IsDestination_DernierSaut_EstMarqueCommeAtteint()
    {
        var hop = new TracerouteHop(8, "35.190.1.1", 45.0, true);

        Assert.True(hop.IsDestination);
    }
}
