using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class HibpServiceTests
{
    [Fact]
    public void HashAndSplit_PasswordConnu_ProduitLeSha1Attendu()
    {
        // SHA-1("password") = 5BAA61E4C9B93F3F0682250B6CF8331B7EE68FD8
        var (prefix, suffix) = HibpService.HashAndSplit("password");

        Assert.Equal("5BAA6", prefix);
        Assert.Equal("1E4C9B93F3F0682250B6CF8331B7EE68FD8", suffix);
    }

    [Fact]
    public void CountMatches_SuffixePresent_RetourneLeCompte()
    {
        const string response = "0018A45C4D1DEF81644B54AB7F969B88D65:1\r\n1E4C9B93F3F0682250B6CF8331B7EE68FD8:10437277\r\nFFFFFF:2";

        var count = HibpService.CountMatches(response, "1E4C9B93F3F0682250B6CF8331B7EE68FD8");

        Assert.Equal(10437277, count);
    }

    [Fact]
    public void CountMatches_SuffixeAbsent_RetourneZero()
    {
        const string response = "0018A45C4D1DEF81644B54AB7F969B88D65:1";

        Assert.Equal(0, HibpService.CountMatches(response, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    [Fact]
    public void CountMatches_InsensibleALaCasse()
    {
        const string response = "1e4c9b93f3f0682250b6cf8331b7ee68fd8:42";

        Assert.Equal(42, HibpService.CountMatches(response, "1E4C9B93F3F0682250B6CF8331B7EE68FD8"));
    }
}
