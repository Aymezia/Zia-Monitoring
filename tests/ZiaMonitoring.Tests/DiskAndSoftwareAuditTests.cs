using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class TrimStatusServiceTests
{
    [Fact]
    public void ParseOutput_TrimActive_Francais()
    {
        const string output = "NTFS DisableDeleteNotify = 0   (Autorise l'envoi d'opérations TRIM au dispositif de stockage)\r\n" +
                               "ReFS DisableDeleteNotify = 0   (Autorise l'envoi d'opérations TRIM au dispositif de stockage)\r\n";

        var status = TrimStatusService.ParseOutput(output);

        Assert.NotNull(status);
        Assert.True(status!.NtfsTrimEnabled);
        Assert.True(status.RefsTrimEnabled);
    }

    [Fact]
    public void ParseOutput_TrimDesactive()
    {
        const string output = "NTFS DisableDeleteNotify = 1   (some text)\r\nReFS DisableDeleteNotify = 1   (some text)\r\n";

        var status = TrimStatusService.ParseOutput(output);

        Assert.NotNull(status);
        Assert.False(status!.NtfsTrimEnabled);
        Assert.False(status.RefsTrimEnabled);
    }

    [Fact]
    public void ParseOutput_SortieVide_RetourneNull()
    {
        Assert.Null(TrimStatusService.ParseOutput(""));
    }

    [Fact]
    public void ParseOutput_SortieInattendue_RetourneNull()
    {
        Assert.Null(TrimStatusService.ParseOutput("erreur inattendue sans le mot-clé"));
    }
}

public sealed class DiskOptimizationAuditServiceTests
{
    [Theory]
    [InlineData(3, "HDD")]
    [InlineData(4, "SSD")]
    [InlineData(5, "SCM")]
    [InlineData(0, "Non spécifié")]
    [InlineData(99, "Non spécifié")]
    public void MediaTypeLabel_MappeLesValeursConnues(ushort mediaType, string expected)
    {
        Assert.Equal(expected, DiskOptimizationAuditService.MediaTypeLabel(mediaType));
    }

    [Fact]
    public void IsEnabledFromTaskXml_EnabledFalse_RetourneFalse()
    {
        const string xml = "<Task><Settings><Enabled>false</Enabled></Settings></Task>";
        Assert.False(DiskOptimizationAuditService.IsEnabledFromTaskXml(xml));
    }

    [Fact]
    public void IsEnabledFromTaskXml_PasDeBaliseEnabled_ActivePardefaut()
    {
        // Cas réel observé : ScheduledDefrag n'a pas de <Enabled> dans <Settings>
        // (activée par défaut selon le schéma Task Scheduler).
        const string xml = "<Task><Settings><DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries></Settings></Task>";
        Assert.True(DiskOptimizationAuditService.IsEnabledFromTaskXml(xml));
    }

    [Fact]
    public void IsEnabledFromTaskXml_XmlVide_ActiveParPrudence()
    {
        Assert.True(DiskOptimizationAuditService.IsEnabledFromTaskXml(""));
    }
}

public sealed class InstalledSoftwareAuditServiceTests
{
    [Theory]
    [InlineData("Opera GX 100.0.4815.59", "opera gx")]
    [InlineData("Steam Client (x64)", "steam client")]
    [InlineData("Discord 1.0.9037", "discord")]
    [InlineData("Adobe Reader 2022", "adobe reader")]
    public void NormalizeName_RetireVersionEtArchitecture(string name, string expected)
    {
        Assert.Equal(expected, InstalledSoftwareAuditService.NormalizeName(name));
    }

    [Theory]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64)")]
    [InlineData("Microsoft .NET Runtime 8.0.1 (x64)")]
    [InlineData("DirectX 9c")]
    public void IsExcludedFamily_FamillesRedistribuables_Vrai(string name)
    {
        Assert.True(InstalledSoftwareAuditService.IsExcludedFamily(name));
    }

    [Fact]
    public void IsExcludedFamily_LogicielOrdinaire_Faux()
    {
        Assert.False(InstalledSoftwareAuditService.IsExcludedFamily("Opera GX"));
    }

    [Theory]
    [InlineData("WinRAR Trial")]
    [InlineData("Logiciel version d'essai")]
    [InlineData("Démo du jeu")]
    public void IsTrialCandidate_MotsClesConnus_Vrai(string name)
    {
        Assert.True(InstalledSoftwareAuditService.IsTrialCandidate(name));
    }

    [Fact]
    public void IsTrialCandidate_LogicielOrdinaire_Faux()
    {
        Assert.False(InstalledSoftwareAuditService.IsTrialCandidate("Opera GX"));
    }

    [Fact]
    public void DetectDuplicates_MemeLogicielDeuxEmplacements_Detecte()
    {
        var entries = new[]
        {
            new InstalledSoftwareEntry("MonApp", "Editeur", @"C:\Program Files\MonApp", "1.0"),
            new InstalledSoftwareEntry("MonApp", "Editeur", @"C:\Program Files (x86)\MonApp", "1.0")
        };

        var duplicates = InstalledSoftwareAuditService.DetectDuplicates(entries);

        var group = Assert.Single(duplicates);
        Assert.Equal(2, group.InstallLocations.Count);
    }

    [Fact]
    public void DetectDuplicates_MemeEmplacement_PasDeDoublon()
    {
        var entries = new[]
        {
            new InstalledSoftwareEntry("MonApp", "Editeur", @"C:\Program Files\MonApp", "1.0"),
            new InstalledSoftwareEntry("MonApp", "Editeur", @"C:\Program Files\MonApp", "1.0")
        };

        Assert.Empty(InstalledSoftwareAuditService.DetectDuplicates(entries));
    }

    [Fact]
    public void DetectDuplicates_FamilleExclue_JamaisSignalee()
    {
        var entries = new[]
        {
            new InstalledSoftwareEntry("Microsoft Visual C++ 2015-2022 Redistributable (x86)", "Microsoft", @"C:\A", "14.0"),
            new InstalledSoftwareEntry("Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft", @"C:\B", "14.0")
        };

        Assert.Empty(InstalledSoftwareAuditService.DetectDuplicates(entries));
    }

    [Fact]
    public void DetectDuplicates_SansEmplacement_Ignore()
    {
        var entries = new[]
        {
            new InstalledSoftwareEntry("MonApp", "Editeur", null, "1.0"),
            new InstalledSoftwareEntry("MonApp", "Editeur", null, "1.0")
        };

        Assert.Empty(InstalledSoftwareAuditService.DetectDuplicates(entries));
    }
}

public sealed class KnownIssueServiceTests
{
    [Fact]
    public void MatchBugcheck_CodeConnu_RetourneExplicationEtLien()
    {
        var issue = KnownIssueService.MatchBugcheck("Le système a redémarré après une erreur fatale : 0x0000001E (IRQL_NOT_LESS_OR_EQUAL, ...)");

        Assert.NotNull(issue);
        Assert.Contains("learn.microsoft.com", issue!.Url);
    }

    [Fact]
    public void MatchBugcheck_CodeInconnu_RetourneNull()
    {
        Assert.Null(KnownIssueService.MatchBugcheck("UN_CODE_INVENTE_INCONNU"));
    }

    [Fact]
    public void MatchBugcheck_TexteVide_RetourneNull()
    {
        Assert.Null(KnownIssueService.MatchBugcheck(""));
        Assert.Null(KnownIssueService.MatchBugcheck(null));
    }

    [Fact]
    public void MatchFaultingModule_ModuleNvidia_RetourneVendorEtLien()
    {
        var issue = KnownIssueService.MatchFaultingModule("nvlddmkm.sys");

        Assert.NotNull(issue);
        Assert.Equal("NVIDIA", issue!.Title);
    }

    [Fact]
    public void MatchFaultingModule_ModuleInconnu_RetourneNull()
    {
        Assert.Null(KnownIssueService.MatchFaultingModule("monmodule.dll"));
    }

    [Fact]
    public void MatchFaultingModule_Inconnu_RetourneNull()
    {
        Assert.Null(KnownIssueService.MatchFaultingModule("inconnu"));
    }
}

public sealed class PcHealthReportServiceTests
{
    [Fact]
    public void IsDue_Desactive_Faux()
    {
        Assert.False(PcHealthReportService.IsDue(enabled: false, lastGeneratedAt: null, now: DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_JamaisGenere_Vrai()
    {
        Assert.True(PcHealthReportService.IsDue(enabled: true, lastGeneratedAt: null, now: DateTime.UtcNow));
    }

    [Fact]
    public void IsDue_MoinsDeSeptJours_Faux()
    {
        var now = DateTime.UtcNow;
        Assert.False(PcHealthReportService.IsDue(enabled: true, lastGeneratedAt: now.AddDays(-3), now: now));
    }

    [Fact]
    public void IsDue_SeptJoursOuPlus_Vrai()
    {
        var now = DateTime.UtcNow;
        Assert.True(PcHealthReportService.IsDue(enabled: true, lastGeneratedAt: now.AddDays(-7), now: now));
    }

    [Fact]
    public void BuildHtml_ContientScoreEtConstats()
    {
        var findings = new List<PcAuditFinding> { new(AuditSeverity.Warning, AuditCategory.Materiel, "Titre test", "Detail test", "Recommandation test") };
        var report = new PcAuditReport(DateTime.Now, 82, findings);
        var history = new List<(DateTime Date, int Score)> { (DateTime.Today.AddDays(-7), 75), (DateTime.Today, 82) };

        var html = PcHealthReportService.BuildHtml(report, history);

        Assert.Contains("82/100", html);
        Assert.Contains("Titre test", html);
    }

    [Fact]
    public void BuildPdf_ProduitUnDocumentNonVide()
    {
        var report = new PcAuditReport(DateTime.Now, 90, []);
        var pdf = PcHealthReportService.BuildPdf(report, []);

        Assert.NotEmpty(pdf);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}

public sealed class GameSaveBackupServiceRestoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void TestRestoreLatestBackup_AucuneSauvegarde_EchecExplicite()
    {
        Directory.CreateDirectory(_dir);
        var service = new GameSaveBackupService();

        var result = service.TestRestoreLatestBackup(_dir);

        Assert.False(result.Success);
        Assert.Contains("Aucune sauvegarde", result.Message);
    }

    [Fact]
    public void TestRestoreLatestBackup_SauvegardeValide_Reussit()
    {
        Directory.CreateDirectory(_dir);
        var zipPath = Path.Combine(_dir, "saves-2026-01-01_0000.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("MyGames/save1.dat");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("contenu de test");
        }

        var service = new GameSaveBackupService();
        var result = service.TestRestoreLatestBackup(_dir);

        Assert.True(result.Success);
        Assert.Contains("1 fichier(s)", result.Message);
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "shouldnotexist")));
    }

    [Fact]
    public void TestRestoreLatestBackup_ArchiveCorrompue_EchecExplicite()
    {
        Directory.CreateDirectory(_dir);
        var zipPath = Path.Combine(_dir, "saves-2026-01-01_0000.zip");
        File.WriteAllText(zipPath, "ceci n'est pas un zip valide");

        var service = new GameSaveBackupService();
        var result = service.TestRestoreLatestBackup(_dir);

        Assert.False(result.Success);
    }
}
