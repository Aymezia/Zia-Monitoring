using System.Net;
using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class ActiveGameDetectorTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("51.91.10.20", true)]
    [InlineData("172.32.0.1", true)]  // hors plage privée 172.16-31
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("127.0.0.1", false)]
    public void IsPublicAddress_ClasseCorrectementLesAdressesIpv4(string ip, bool expected)
    {
        Assert.Equal(expected, ActiveGameDetector.IsPublicAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPublicAddress_Ipv6_RetourneFalse()
    {
        // Comportement actuel : seules les adresses IPv4 sont considérées.
        Assert.False(ActiveGameDetector.IsPublicAddress(IPAddress.Parse("2001:4860:4860::8888")));
        Assert.False(ActiveGameDetector.IsPublicAddress(IPAddress.IPv6Loopback));
    }

    private static TcpConnectionInfo Conn(int pid, string remote, string state = "Established")
        => new(pid, "game", "192.168.1.10:51000", remote, state);

    [Fact]
    public void FindLikelyGameServer_RetourneLaConnexionPubliqueEtablieDuProcessus()
    {
        var connections = new[]
        {
            Conn(100, "192.168.1.50:443"),          // privée : ignorée
            Conn(200, "8.8.8.8:7000"),               // mauvais PID
            Conn(100, "51.91.10.20:7000"),           // candidate attendue
        };

        var result = ActiveGameDetector.FindLikelyGameServer(connections, 100);

        Assert.Equal("51.91.10.20:7000", result);
    }

    [Fact]
    public void FindLikelyGameServer_IgnoreLesConnexionsNonEtablies()
    {
        var connections = new[] { Conn(100, "51.91.10.20:7000", state: "TimeWait") };

        Assert.Null(ActiveGameDetector.FindLikelyGameServer(connections, 100));
    }

    [Fact]
    public void FindLikelyGameServer_AucuneCandidate_RetourneNull()
    {
        Assert.Null(ActiveGameDetector.FindLikelyGameServer([], 100));
    }
}
