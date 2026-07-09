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

    [Fact]
    public void ResumeArgs_AllerRetour_Clean()
    {
        var args = DebloatService.BuildResumeArgs(DebloatCategory.Telemetry, "DiagTrack", isUndo: false);

        var parsed = DebloatService.TryParseResumeArgs(["ZiaMonitoring.App.exe", .. args]);

        Assert.NotNull(parsed);
        Assert.False(parsed!.IsCleanAll);
        Assert.False(parsed.IsUndo);
        Assert.Equal(DebloatCategory.Telemetry, parsed.Category);
        Assert.Equal("DiagTrack", parsed.Key);
    }

    [Fact]
    public void ResumeArgs_AllerRetour_UndoAvecCleContenantEspacesEtAntislashs()
    {
        const string key = @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser";
        var args = DebloatService.BuildResumeArgs(DebloatCategory.ScheduledTask, key, isUndo: true);

        var parsed = DebloatService.TryParseResumeArgs(["ZiaMonitoring.App.exe", .. args]);

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsUndo);
        Assert.Equal(DebloatCategory.ScheduledTask, parsed.Category);
        Assert.Equal(key, parsed.Key);
    }

    [Fact]
    public void ResumeArgs_AllerRetour_CleanAll()
    {
        var args = DebloatService.BuildResumeAllArgs();

        var parsed = DebloatService.TryParseResumeArgs(["ZiaMonitoring.App.exe", .. args]);

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsCleanAll);
        Assert.Null(parsed.Category);
        Assert.Null(parsed.Key);
    }

    [Fact]
    public void TryParseResumeArgs_AucunFlag_RetourneNull()
    {
        Assert.Null(DebloatService.TryParseResumeArgs(["ZiaMonitoring.App.exe"]));
    }

    [Fact]
    public void TryParseResumeArgs_FlagIncomplet_RetourneNull()
    {
        Assert.Null(DebloatService.TryParseResumeArgs(["ZiaMonitoring.App.exe", "--resume-debloat", "clean", "Telemetry"]));
    }
}
