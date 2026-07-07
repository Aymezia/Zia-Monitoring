using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class DriverVendorTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", "NVIDIA")]
    [InlineData("AMD Radeon RX 7800 XT", "AMD")]
    [InlineData("Intel(R) Wi-Fi 6 AX201", "Intel")]
    [InlineData("Realtek High Definition Audio", "Realtek")]
    [InlineData("Logitech G HUB Peripheral", "Logitech")]
    [InlineData("Corsair iCUE Device", "Corsair")]
    public void DetectDriverVendor_NomConnu_RetourneLeBonFabricant(string driverName, string expectedVendor)
    {
        var (vendor, _) = SecurityScanService.DetectDriverVendor(driverName);

        Assert.Equal(expectedVendor, vendor);
    }

    [Fact]
    public void DetectDriverVendor_NomInconnu_RetourneLeRepliGenerique()
    {
        var (vendor, url) = SecurityScanService.DetectDriverVendor("Generic USB Hub Driver");

        Assert.Equal("Fabricant inconnu", vendor);
        Assert.Contains("microsoft.com", url);
    }

    [Fact]
    public void DetectDriverVendor_RetourneUneUrlHttpsValide()
    {
        var (_, url) = SecurityScanService.DetectDriverVendor("NVIDIA GeForce RTX 4070");

        Assert.StartsWith("https://", url);
    }
}
