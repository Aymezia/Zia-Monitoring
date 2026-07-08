using ZiaMonitoring_App.Infrastructure.Collectors;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class PcProfileCollectorTests
{
    [Fact]
    public void CleanCpuName_RetireLesEspacesDePaddingWmi()
    {
        var result = PcProfileCollector.CleanCpuName("AMD Ryzen 5 5500                          ");

        Assert.Equal("AMD Ryzen 5 5500", result);
    }

    [Fact]
    public void CleanCpuName_ConserveUnSeulEspaceEntreLesMots()
    {
        var result = PcProfileCollector.CleanCpuName("Intel(R)   Core(TM)   i7-12700K");

        Assert.Equal("Intel(R) Core(TM) i7-12700K", result);
    }

    [Theory]
    [InlineData("Parsec Virtual Display Adapter")]
    [InlineData("TeamViewer")]
    [InlineData("Microsoft Basic Render Driver")]
    [InlineData("Microsoft Basic Display Adapter")]
    [InlineData("Remote Desktop Adapter")]
    public void IsVirtualAdapter_DetecteLesAdaptateursVirtuelsConnus(string name)
    {
        Assert.True(PcProfileCollector.IsVirtualAdapter(name));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Ti")]
    [InlineData("AMD Radeon RX 6600")]
    [InlineData("Intel(R) UHD Graphics 770")]
    public void IsVirtualAdapter_NeDetectePasLesGpuPhysiques(string name)
    {
        Assert.False(PcProfileCollector.IsVirtualAdapter(name));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4060 Ti")]
    [InlineData("AMD Radeon RX 6600")]
    [InlineData("Intel(R) Iris Xe Graphics")]
    public void IsKnownGpuVendor_ReconnaitLesMarquesGpuCourantes(string name)
    {
        Assert.True(PcProfileCollector.IsKnownGpuVendor(name));
    }

    [Fact]
    public void IsKnownGpuVendor_NeReconnaitPasUnAdaptateurVirtuel()
    {
        Assert.False(PcProfileCollector.IsKnownGpuVendor("Parsec Virtual Display Adapter"));
    }
}
