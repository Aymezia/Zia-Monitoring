using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class StartupManagerServiceTests
{
    [Fact]
    public void IsApprovedEnabled_ValeurAbsente_EstActive()
    {
        Assert.True(StartupManagerService.IsApprovedEnabled(null));
        Assert.True(StartupManagerService.IsApprovedEnabled([]));
    }

    [Theory]
    [InlineData((byte)0x02, true)]   // activé (défaut Gestionnaire des tâches)
    [InlineData((byte)0x06, true)]   // réactivé
    [InlineData((byte)0x03, false)]  // désactivé
    public void IsApprovedEnabled_SuitLaConventionPremierOctet(byte first, bool expected)
    {
        var data = new byte[12];
        data[0] = first;

        Assert.Equal(expected, StartupManagerService.IsApprovedEnabled(data));
    }

    [Fact]
    public void BuildApprovedValue_RoundTripAvecIsApprovedEnabled()
    {
        Assert.True(StartupManagerService.IsApprovedEnabled(StartupManagerService.BuildApprovedValue(true)));
        Assert.False(StartupManagerService.IsApprovedEnabled(StartupManagerService.BuildApprovedValue(false)));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", "C:\\Program Files\\App\\app.exe")]
    [InlineData("C:\\Tools\\tool.exe /silent", "C:\\Tools\\tool.exe")]
    [InlineData("C:\\Tools\\tool.exe", "C:\\Tools\\tool.exe")]
    public void ExtractExecutablePath_GereGuillemetsEtArguments(string command, string expected)
    {
        Assert.Equal(expected, StartupManagerService.ExtractExecutablePath(command));
    }

    [Fact]
    public void EstimateImpact_AppConnueLourde_RetourneEleve()
    {
        Assert.Equal("élevé", StartupManagerService.EstimateImpact(@"C:\Users\x\AppData\Local\Discord\Update.exe --processStart Discord.exe"));
    }
}
