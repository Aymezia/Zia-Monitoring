using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class ThrottlingDetectorTests
{
    private static void Warmup(ThrottlingDetector detector)
    {
        // Établit la fréquence max observée : 4500 MHz CPU / 2500 MHz GPU sous charge saine.
        for (var i = 0; i < 5; i++)
            detector.Update(cpuTempC: 60, cpuClockMhz: 4500, cpuLoadPercent: 80,
                            gpuTempC: 55, gpuClockMhz: 2500, gpuLoadPercent: 80);
    }

    [Fact]
    public void Update_ChargeNormale_AucuneAlerte()
    {
        var detector = new ThrottlingDetector();
        Warmup(detector);

        var toast = detector.Update(70, 4400, 90, 65, 2450, 90);

        Assert.Null(toast);
        Assert.Null(detector.ActiveAlert);
    }

    [Fact]
    public void Update_ChuteDeFrequenceSousChargeChaude_ConfirmeApresHysteresis()
    {
        var detector = new ThrottlingDetector();
        Warmup(detector);

        string? toast = null;
        // Temp critique + charge haute + horloge à 3000 (< 82 % de 4500).
        for (var i = 0; i < 10; i++)
            toast = detector.Update(95, 3000, 95, 60, 2450, 50);

        Assert.NotNull(detector.ActiveAlert);
        Assert.Contains("CPU", detector.ActiveAlert);
        Assert.NotNull(toast); // toast émis au moment de la confirmation
    }

    [Fact]
    public void Update_UnSeulCycleChaud_PasDAlerte()
    {
        var detector = new ThrottlingDetector();
        Warmup(detector);

        var toast = detector.Update(95, 3000, 95, 60, 2450, 50);

        Assert.Null(toast);
        Assert.Null(detector.ActiveAlert);
    }

    [Fact]
    public void Update_Recuperation_ReinitialiseLAlerte()
    {
        var detector = new ThrottlingDetector();
        Warmup(detector);

        for (var i = 0; i < 10; i++)
            detector.Update(95, 3000, 95, 60, 2450, 50);
        Assert.NotNull(detector.ActiveAlert);

        // Refroidissement franc → l'alerte disparaît.
        detector.Update(60, 4400, 30, 55, 2450, 20);

        Assert.Null(detector.ActiveAlert);
    }

    [Fact]
    public void Update_SansCapteurs_NeDetecteJamais()
    {
        var detector = new ThrottlingDetector();

        for (var i = 0; i < 20; i++)
            Assert.Null(detector.Update(null, null, 95, null, null, null));

        Assert.Null(detector.ActiveAlert);
    }
}
