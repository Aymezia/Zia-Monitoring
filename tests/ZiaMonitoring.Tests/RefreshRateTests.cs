using ZiaMonitoring_App.Infrastructure.Collectors;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class RefreshRateTests
{
    [Fact]
    public void IsMismatch_60HzActifSur144HzDisponible_DetecteLeProbleme()
    {
        var reading = new RefreshRateReading("\\\\.\\DISPLAY1", "Écran principal", 60, 144);

        Assert.True(reading.IsMismatch);
        Assert.False(reading.IsHealthy);
        Assert.Contains("passez en 144 Hz", reading.Label);
    }

    [Fact]
    public void IsMismatch_DejaAuMaximum_AucunProbleme()
    {
        var reading = new RefreshRateReading("\\\\.\\DISPLAY1", "Écran principal", 144, 144);

        Assert.False(reading.IsMismatch);
        Assert.True(reading.IsHealthy);
    }

    [Fact]
    public void IsMismatch_PetitEcartVrr_NestPasSignale()
    {
        // 143 vs 144 Hz : variation normale de pilote, pas un vrai mismatch.
        var reading = new RefreshRateReading("\\\\.\\DISPLAY1", "Écran principal", 143, 144);

        Assert.False(reading.IsMismatch);
    }

    [Fact]
    public void IsMismatch_EcartJusteSousLeSeuil_NestPasSignale()
    {
        var reading = new RefreshRateReading("\\\\.\\DISPLAY1", "Écran principal", 125, 144);

        Assert.False(reading.IsMismatch);
    }

    [Fact]
    public void IsMismatch_EcartJusteAuSeuil_EstSignale()
    {
        var reading = new RefreshRateReading("\\\\.\\DISPLAY1", "Écran principal", 124, 144);

        Assert.True(reading.IsMismatch);
    }
}
