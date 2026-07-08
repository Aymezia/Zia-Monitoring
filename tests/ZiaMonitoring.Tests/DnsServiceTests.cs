using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class DnsServiceTests
{
    [Theory]
    [InlineData("WireGuard Tunnel", "")]
    [InlineData("Ethernet 2", "TAP-Windows Adapter V9")]
    [InlineData("NordLynx", "")]
    [InlineData("Local Area Connection", "Cisco AnyConnect Secure Mobility Client")]
    public void IsVpnAdapter_DetecteLesAdaptateursVpnConnus(string name, string description)
    {
        Assert.True(DnsService.IsVpnAdapter(name, description));
    }

    [Theory]
    [InlineData("Ethernet", "Realtek PCIe GbE Family Controller")]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6 AX201")]
    public void IsVpnAdapter_NeDetectePasUnAdaptateurPhysique(string name, string description)
    {
        Assert.False(DnsService.IsVpnAdapter(name, description));
    }

    [Fact]
    public void MarkFastest_MarqueLeResultatJoignableLePlusRapide()
    {
        var results = new[]
        {
            new DnsLatencyResult("Cloudflare", "1.1.1.1", 12, true),
            new DnsLatencyResult("Google", "8.8.8.8", 8, true),
            new DnsLatencyResult("Quad9", "9.9.9.9", null, false)
        };

        var marked = DnsService.MarkFastest(results);

        Assert.True(marked.Single(r => r.Label == "Google").IsBest);
        Assert.False(marked.Single(r => r.Label == "Cloudflare").IsBest);
        Assert.False(marked.Single(r => r.Label == "Quad9").IsBest);
    }

    [Fact]
    public void MarkFastest_AucunJoignable_NeMarquePersonne()
    {
        var results = new[]
        {
            new DnsLatencyResult("Cloudflare", "1.1.1.1", null, false),
            new DnsLatencyResult("Google", "8.8.8.8", null, false)
        };

        var marked = DnsService.MarkFastest(results);

        Assert.All(marked, r => Assert.False(r.IsBest));
    }

    [Fact]
    public void Providers_ContientAutomatiqueEtLes3FournisseursPublics()
    {
        Assert.Equal(4, DnsService.Providers.Count);
        Assert.Contains(DnsService.Providers, p => p.Provider == DnsProvider.Automatic && p.PrimaryIp is null);
        Assert.Contains(DnsService.Providers, p => p.Provider == DnsProvider.Cloudflare && p.PrimaryIp == "1.1.1.1");
        Assert.Contains(DnsService.Providers, p => p.Provider == DnsProvider.Google && p.PrimaryIp == "8.8.8.8");
        Assert.Contains(DnsService.Providers, p => p.Provider == DnsProvider.Quad9 && p.PrimaryIp == "9.9.9.9");
    }
}
