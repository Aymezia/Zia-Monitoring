using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class GeoIpServiceTests
{
    [Theory]
    [InlineData("8.8.8.8:443", "8.8.8.8")]
    [InlineData("51.91.10.20:7000", "51.91.10.20")]
    [InlineData("8.8.8.8", "8.8.8.8")]
    public void ExtractPublicIp_AdressePublique_RetourneLIpSansPort(string endpoint, string expected)
    {
        Assert.Equal(expected, GeoIpService.ExtractPublicIp(endpoint));
    }

    [Theory]
    [InlineData("192.168.1.10:443")]
    [InlineData("10.0.0.1:80")]
    [InlineData("127.0.0.1:9000")]
    [InlineData("pas-une-ip:443")]
    [InlineData("")]
    public void ExtractPublicIp_PriveeOuInvalide_RetourneNull(string endpoint)
    {
        Assert.Null(GeoIpService.ExtractPublicIp(endpoint));
    }

    [Fact]
    public void ParseBatchResponse_ReponsesValides_ParseesEtEchecsIgnores()
    {
        const string json = """
            [
              {"status":"success","country":"France","countryCode":"FR","org":"OVH SAS","isp":"OVH","query":"51.91.10.20"},
              {"status":"fail","message":"private range","query":"192.168.1.1"},
              {"status":"success","country":"United States","countryCode":"US","org":"","isp":"Google LLC","query":"8.8.8.8"}
            ]
            """;

        var results = GeoIpService.ParseBatchResponse(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("France", results[0].Country);
        Assert.Contains("OVH", results[0].Label);
        // org vide → repli sur isp.
        Assert.Equal("Google LLC", results[1].Org);
    }
}
