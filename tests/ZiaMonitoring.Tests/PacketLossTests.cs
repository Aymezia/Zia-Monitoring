using ZiaMonitoring_App.Infrastructure.Collectors;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class PacketLossTests
{
    [Fact]
    public void ComputeLossStats_AucunPaquetPerdu_ZeroPourcent()
    {
        var samples = new double?[] { 20, 22, 19, 21 };

        var (loss, avgRtt) = GameServerLatencyCollector.ComputeLossStats(samples);

        Assert.Equal(0, loss);
        Assert.Equal(20.5, avgRtt);
    }

    [Fact]
    public void ComputeLossStats_MoitiePerdue_CinquantePourcent()
    {
        var samples = new double?[] { 20, null, 30, null };

        var (loss, avgRtt) = GameServerLatencyCollector.ComputeLossStats(samples);

        Assert.Equal(50, loss);
        Assert.Equal(25, avgRtt);
    }

    [Fact]
    public void ComputeLossStats_ToutPerdu_CentPourcentEtRttInvalide()
    {
        var samples = new double?[] { null, null, null };

        var (loss, avgRtt) = GameServerLatencyCollector.ComputeLossStats(samples);

        Assert.Equal(100, loss);
        Assert.Equal(-1, avgRtt);
    }

    [Fact]
    public void ComputeLossStats_ListeVide_ZeroSansErreur()
    {
        var (loss, avgRtt) = GameServerLatencyCollector.ComputeLossStats(Array.Empty<double?>());

        Assert.Equal(0, loss);
        Assert.Equal(-1, avgRtt);
    }

    [Fact]
    public void PacketLossReading_SansDonnees_AfficheIcmpBloque()
    {
        var reading = new PacketLossReading("Riot", "riotgames.com", 100, -1, HasData: false);

        Assert.Equal("N/A (ICMP bloqué)", reading.LossLabel);
        Assert.Equal("-", reading.RttLabel);
    }

    [Fact]
    public void PacketLossReading_AvecDonnees_AfficheLePourcentageEtLeRtt()
    {
        var reading = new PacketLossReading("Riot", "riotgames.com", 20, 35.4, HasData: true);

        Assert.Equal("20% perte", reading.LossLabel);
        Assert.Equal("35 ms", reading.RttLabel);
    }
}
