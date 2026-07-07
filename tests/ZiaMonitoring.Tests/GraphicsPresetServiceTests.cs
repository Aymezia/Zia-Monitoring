using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class GraphicsPresetServiceTests
{
    private readonly GraphicsPresetService _service = new();

    // Score = VRAM(0/10/20/30/40) + Coeurs(0/5/10/15/20) + RAM(0/5/15/20) ; seuils 25/45/70.
    [Theory]
    [InlineData(2000, 4, 8, HardwareTier.Entry)]      // 0 + 5 + 5 = 10
    [InlineData(6000, 4, 8, HardwareTier.MidRange)]   // 20 + 5 + 5 = 30
    [InlineData(10000, 8, 16, HardwareTier.HighEnd)]  // 30 + 15 + 15 = 60
    [InlineData(16000, 16, 32, HardwareTier.Enthusiast)] // 40 + 20 + 20 = 80
    public void DetectTier_CombinaisonMaterielle_DonneLePalierAttendu(
        double vramMb, int cores, double ramGb, HardwareTier expected)
    {
        Assert.Equal(expected, _service.DetectTier(vramMb, cores, ramGb));
    }

    [Fact]
    public void DetectTier_MaterielFaible_RetourneEntry()
    {
        Assert.Equal(HardwareTier.Entry, _service.DetectTier(0, 2, 4));
    }

    [Theory]
    [InlineData(HardwareTier.Entry)]
    [InlineData(HardwareTier.MidRange)]
    [InlineData(HardwareTier.HighEnd)]
    [InlineData(HardwareTier.Enthusiast)]
    public void GetPreset_ChaquePalier_RetourneUnPresetComplet(HardwareTier tier)
    {
        var preset = _service.GetPreset(tier);

        Assert.Equal(tier, preset.Tier);
        Assert.NotEmpty(preset.TierLabel);
        Assert.NotEmpty(preset.Resolution);
        Assert.NotEmpty(preset.Notes);
    }

    [Fact]
    public void DetectTier_PaliersSontMonotonesCroissants()
    {
        var entry = _service.DetectTier(2000, 4, 8);
        var mid = _service.DetectTier(6000, 4, 8);
        var high = _service.DetectTier(10000, 8, 16);
        var enthusiast = _service.DetectTier(16000, 16, 32);

        Assert.True(entry < mid);
        Assert.True(mid < high);
        Assert.True(high < enthusiast);
    }
}
