using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class PresentMonServiceTests
{
    [Fact]
    public void FindFrametimeColumn_FormatV2_TrouveFrameTime()
    {
        const string header = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,AllowsTearing,PresentMode,CPUStartTime,FrameTime,CPUBusy";

        Assert.Equal(9, PresentMonService.FindFrametimeColumn(header));
    }

    [Fact]
    public void FindFrametimeColumn_FormatV1_TrouveMsBetweenPresents()
    {
        const string header = "Application,ProcessID,SwapChainAddress,Runtime,SyncInterval,PresentFlags,Dropped,TimeInSeconds,msInPresentAPI,msBetweenPresents";

        Assert.Equal(9, PresentMonService.FindFrametimeColumn(header));
    }

    [Fact]
    public void FindFrametimeColumn_EnTeteInconnue_RetourneMoinsUn()
    {
        Assert.Equal(-1, PresentMonService.FindFrametimeColumn("a,b,c"));
        Assert.Equal(-1, PresentMonService.FindFrametimeColumn(null));
    }

    [Fact]
    public void ParseFrametimeMs_ValeurInvariante_Parsee()
    {
        Assert.Equal(16.67, PresentMonService.ParseFrametimeMs("game.exe,1234,0x0,DXGI,16.67", 4));
    }

    [Fact]
    public void ParseFrametimeMs_ColonneManquanteOuInvalide_RetourneNull()
    {
        Assert.Null(PresentMonService.ParseFrametimeMs("game.exe,1234", 5));
        Assert.Null(PresentMonService.ParseFrametimeMs("game.exe,abc", 1));
    }
}
