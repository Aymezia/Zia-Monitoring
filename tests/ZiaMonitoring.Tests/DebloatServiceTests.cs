using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class DebloatServiceTests
{
    [Fact]
    public void ParseTaskEnabledFromCsv_TacheDesactivee_RetourneFalse()
    {
        const string csv = "\"\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser\",\"N/A\",\"Disabled\"";

        Assert.False(DebloatService.ParseTaskEnabledFromCsv(csv));
    }

    [Theory]
    [InlineData("\"MonTask\",\"01/01/2026 03:00:00\",\"Ready\"")]
    [InlineData("\"MonTask\",\"N/A\",\"Running\"")]
    public void ParseTaskEnabledFromCsv_TacheActive_RetourneTrue(string csv)
    {
        Assert.True(DebloatService.ParseTaskEnabledFromCsv(csv));
    }

    [Fact]
    public void ParseTaskEnabledFromCsv_LigneVide_RetourneFalse()
    {
        Assert.False(DebloatService.ParseTaskEnabledFromCsv(string.Empty));
    }
}
