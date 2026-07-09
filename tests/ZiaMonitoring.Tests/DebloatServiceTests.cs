using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class DebloatServiceTests
{
    // schtasks /Query /FO CSV renvoie un statut localisé ("Désactivé" en
    // français) : comparer au seul mot anglais "Disabled" faisait paraître
    // TOUTE tâche désactivée comme "encore active" sur une machine non
    // anglophone. /XML expose <Enabled>true|false</Enabled>, structurel et
    // indépendant de la langue d'affichage — capture réelle sur une tâche
    // française désactivée (Microsoft-Windows-DiskDiagnosticDataCollector).
    private const string RealDisabledTaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.6" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <URI>\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector</URI>
          </RegistrationInfo>
          <Settings>
            <DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>
            <Enabled>false</Enabled>
            <Hidden>true</Hidden>
          </Settings>
          <Triggers />
          <Actions Context="LocalSystem">
            <Exec>
              <Command>%windir%\system32\rundll32.exe</Command>
            </Exec>
          </Actions>
        </Task>
        """;

    [Fact]
    public void ParseTaskEnabledFromXml_TacheReelleDesactivee_RetourneFalse()
    {
        Assert.False(DebloatService.ParseTaskEnabledFromXml(RealDisabledTaskXml));
    }

    [Fact]
    public void ParseTaskEnabledFromXml_TacheActivee_RetourneTrue()
    {
        const string xml = "<Task><Settings><Enabled>true</Enabled></Settings></Task>";
        Assert.True(DebloatService.ParseTaskEnabledFromXml(xml));
    }

    [Fact]
    public void ParseTaskEnabledFromXml_EnabledAbsentDuBlocSettings_ConsidereActiveParDefaut()
    {
        const string xml = "<Task><Settings><Hidden>true</Hidden></Settings></Task>";
        Assert.True(DebloatService.ParseTaskEnabledFromXml(xml));
    }

    [Fact]
    public void ParseTaskEnabledFromXml_XmlVide_TacheAbsenteConsidereeClean()
    {
        Assert.False(DebloatService.ParseTaskEnabledFromXml(string.Empty));
    }

    [Fact]
    public void ParseTaskEnabledFromXml_PasDeBlocSettings_ConsidereActiveParPrudence()
    {
        Assert.True(DebloatService.ParseTaskEnabledFromXml("<Task><Triggers /></Task>"));
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
