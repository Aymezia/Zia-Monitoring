using System.Text;
using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class BatteryServiceTests
{
    private const string SampleReport = """
        <?xml version="1.0"?>
        <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
          <Batteries>
            <Battery>
              <Id>DELL XVJNP1B</Id>
              <Manufacturer>SMP</Manufacturer>
              <DesignCapacity>54000</DesignCapacity>
              <FullChargeCapacity>43200</FullChargeCapacity>
              <CycleCount>312</CycleCount>
            </Battery>
          </Batteries>
        </BatteryReport>
        """;

    [Fact]
    public void ParseBatteryReport_ExtraitCapacitesUsureEtCycles()
    {
        var batteries = BatteryService.ParseBatteryReport(SampleReport);

        var battery = Assert.Single(batteries);
        Assert.Equal("DELL XVJNP1B", battery.Name);
        Assert.Equal(54000, battery.DesignCapacityMWh);
        Assert.Equal(43200, battery.FullChargeCapacityMWh);
        Assert.Equal(312, battery.CycleCount);
        Assert.Equal(20, battery.WearPercent, precision: 1);
    }

    [Fact]
    public void ParseBatteryReport_SansBatterie_RetourneVide()
    {
        Assert.Empty(BatteryService.ParseBatteryReport("""<?xml version="1.0"?><BatteryReport><Batteries /></BatteryReport>"""));
    }

    [Fact]
    public void ParseBatteryReport_XmlInvalide_RetourneVideSansException()
    {
        Assert.Empty(BatteryService.ParseBatteryReport("pas du xml"));
    }

    [Fact]
    public void GetTargetScheme_SecteurVersHautesPerformances_BatterieVersNormal()
    {
        Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", BatteryService.GetTargetScheme(onAc: true));
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", BatteryService.GetTargetScheme(onAc: false));
    }
}

public sealed class WindowsUpdateHistoryServiceTests
{
    [Theory]
    [InlineData("KB5031354", "5031354")]
    [InlineData("kb123456", "123456")]
    [InlineData(" KB5031354 ", "5031354")]
    public void ExtractKbNumber_FormatValide_RetourneLesChiffres(string input, string expected)
    {
        Assert.Equal(expected, WindowsUpdateHistoryService.ExtractKbNumber(input));
    }

    [Theory]
    [InlineData("5031354")]
    [InlineData("KB")]
    [InlineData("KB50313a4")]
    [InlineData("")]
    public void ExtractKbNumber_FormatInvalide_RetourneNull(string input)
    {
        Assert.Null(WindowsUpdateHistoryService.ExtractKbNumber(input));
    }
}

public sealed class FirewallKillSwitchServiceTests
{
    [Fact]
    public void BuildRuleName_ContientLePrefixeEtLeNomDeFichier()
    {
        var name = FirewallKillSwitchService.BuildRuleName(@"C:\Apps\discord.exe");

        Assert.StartsWith("Zia Kill Switch - discord.exe [", name);
    }

    [Fact]
    public void BuildRuleName_CheminsDifferentsMemeNomDeFichier_NomsDifferents()
    {
        var a = FirewallKillSwitchService.BuildRuleName(@"C:\A\app.exe");
        var b = FirewallKillSwitchService.BuildRuleName(@"C:\B\app.exe");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void BuildRuleName_HashInsensibleALaCasseDuChemin()
    {
        // Le nom affiché garde la casse d'origine, mais le hash discriminant
        // doit être identique pour un même chemin quelle que soit la casse.
        static string Hash(string ruleName) => ruleName[(ruleName.IndexOf('[') + 1)..];

        Assert.Equal(
            Hash(FirewallKillSwitchService.BuildRuleName(@"C:\Apps\Discord.exe")),
            Hash(FirewallKillSwitchService.BuildRuleName(@"c:\apps\discord.exe")));
    }
}

public sealed class BrowserExtensionAuditServiceTests
{
    [Fact]
    public void ResolveLocalizedName_TokenMsg_ResoutDepuisMessagesJson()
    {
        const string messages = """{"appName": {"message": "uBlock Origin"}, "other": {"message": "x"}}""";

        Assert.Equal("uBlock Origin", BrowserExtensionAuditService.ResolveLocalizedName("__MSG_appName__", messages));
    }

    [Fact]
    public void ResolveLocalizedName_CleInsensibleALaCasse()
    {
        const string messages = """{"APPNAME": {"message": "AdGuard"}}""";

        Assert.Equal("AdGuard", BrowserExtensionAuditService.ResolveLocalizedName("__MSG_appName__", messages));
    }

    [Fact]
    public void ResolveLocalizedName_CleAbsente_RetourneNull()
    {
        Assert.Null(BrowserExtensionAuditService.ResolveLocalizedName("__MSG_missing__", """{"appName": {"message": "x"}}"""));
    }

    [Fact]
    public void ParseFirefoxExtensions_FiltreLesModulesSystemeEtLesThemes()
    {
        const string json = """
            {"addons": [
              {"type": "extension", "id": "ublock@raymondhill.net", "version": "1.55", "location": "app-profile", "defaultLocale": {"name": "uBlock Origin"}},
              {"type": "extension", "id": "screenshots@mozilla.org", "version": "39", "location": "app-builtin", "defaultLocale": {"name": "Firefox Screenshots"}},
              {"type": "theme", "id": "default-theme@mozilla.org", "version": "1.3", "location": "app-profile", "defaultLocale": {"name": "Système"}}
            ]}
            """;

        var extensions = BrowserExtensionAuditService.ParseFirefoxExtensions(json, "abc.default");

        var extension = Assert.Single(extensions);
        Assert.Equal("uBlock Origin", extension.Name);
        Assert.Equal("Firefox", extension.Browser);
        Assert.Equal("1.55", extension.Version);
    }
}

public sealed class AppWatchdogServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldRestart_JamaisVueEnVie_NeRelancePas()
    {
        Assert.False(AppWatchdogService.ShouldRestart(seenRunning: false, lastRestartAt: null, restartsLastHour: 0, Now));
    }

    [Fact]
    public void ShouldRestart_VueEnViePuisDisparue_Relance()
    {
        Assert.True(AppWatchdogService.ShouldRestart(seenRunning: true, lastRestartAt: null, restartsLastHour: 0, Now));
    }

    [Fact]
    public void ShouldRestart_RelanceTropRecente_Attend()
    {
        Assert.False(AppWatchdogService.ShouldRestart(seenRunning: true, lastRestartAt: Now.AddSeconds(-30), restartsLastHour: 1, Now));
    }

    [Fact]
    public void ShouldRestart_TropDeRelancesDansLHeure_Abandonne()
    {
        Assert.False(AppWatchdogService.ShouldRestart(seenRunning: true, lastRestartAt: Now.AddMinutes(-10), restartsLastHour: 3, Now));
    }
}

public sealed class SmartRebootServiceTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ShouldPropose_UptimeSousLeSeuil_NeProposePas()
    {
        Assert.False(SmartRebootService.ShouldPropose(TimeSpan.FromDays(3), gameActive: false, null, Now, thresholdDays: 7));
    }

    [Fact]
    public void ShouldPropose_JeuEnCours_NeProposeJamais()
    {
        Assert.False(SmartRebootService.ShouldPropose(TimeSpan.FromDays(30), gameActive: true, null, Now, thresholdDays: 7));
    }

    [Fact]
    public void ShouldPropose_SeuilDepasseSansJeu_Propose()
    {
        Assert.True(SmartRebootService.ShouldPropose(TimeSpan.FromDays(8), gameActive: false, null, Now, thresholdDays: 7));
    }

    [Fact]
    public void ShouldPropose_DejaProposeIlYaMoinsde24h_NeProposePas()
    {
        Assert.False(SmartRebootService.ShouldPropose(TimeSpan.FromDays(8), gameActive: false, Now.AddHours(-3), Now, thresholdDays: 7));
    }

    [Fact]
    public void SecondsUntilNextThreeAm_AvantEtApres3h()
    {
        Assert.Equal(3600, SmartRebootService.SecondsUntilNextThreeAm(new DateTime(2026, 7, 8, 2, 0, 0)));
        Assert.Equal(23 * 3600, SmartRebootService.SecondsUntilNextThreeAm(new DateTime(2026, 7, 8, 4, 0, 0)));
    }
}

public sealed class DiscordRichPresenceServiceTests
{
    [Fact]
    public void BuildFrame_EncodeOpcodeEtLongueurEnLittleEndian()
    {
        const string json = """{"v":1}""";

        var frame = DiscordRichPresenceService.BuildFrame(1, json);

        Assert.Equal(8 + json.Length, frame.Length);
        Assert.Equal(1, BitConverter.ToInt32(frame, 0));
        Assert.Equal(json.Length, BitConverter.ToInt32(frame, 4));
        Assert.Equal(json, Encoding.UTF8.GetString(frame, 8, json.Length));
    }

    [Fact]
    public void BuildFrame_PayloadUtf8MultiOctets_LongueurEnOctetsPasEnCaracteres()
    {
        const string json = """{"état":"géré"}""";
        var byteCount = Encoding.UTF8.GetByteCount(json);

        var frame = DiscordRichPresenceService.BuildFrame(0, json);

        Assert.Equal(byteCount, BitConverter.ToInt32(frame, 4));
        Assert.Equal(8 + byteCount, frame.Length);
    }
}
