using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class MemoryConfigDiagnosticsServiceTests
{
    [Theory]
    [InlineData(3200, 3200, false)]
    [InlineData(3200, 3000, false)]
    [InlineData(3200, 2133, true)]
    [InlineData(0, 3200, false)]
    [InlineData(3200, 0, false)]
    public void IsXmpLikelyDisabled_ComparaisonAvecMarge10Pourcent(int rated, int running, bool expected)
    {
        Assert.Equal(expected, MemoryConfigDiagnosticsService.IsXmpLikelyDisabled(rated, running));
    }

    [Fact]
    public void DiagnoseChannel_AucunModule_Unknown()
    {
        var result = MemoryConfigDiagnosticsService.DiagnoseChannel([]);
        Assert.Equal(ChannelStatus.Unknown, result.Status);
    }

    [Fact]
    public void DiagnoseChannel_UneSeuleBarrette_SingleChannel()
    {
        var result = MemoryConfigDiagnosticsService.DiagnoseChannel(["BANK 0 DIMM0"]);
        Assert.Equal(ChannelStatus.SingleChannel, result.Status);
    }

    [Fact]
    public void DiagnoseChannel_DeuxCanauxDistincts_MultiChannel()
    {
        var result = MemoryConfigDiagnosticsService.DiagnoseChannel(["BANK 0 ChannelA-DIMM0", "BANK 1 ChannelB-DIMM0"]);
        Assert.Equal(ChannelStatus.MultiChannel, result.Status);
        Assert.Equal(2, result.DetectedChannelCount);
    }

    [Fact]
    public void DiagnoseChannel_MemeCanalPourToutesLesBarrettes_Misconfigured()
    {
        var result = MemoryConfigDiagnosticsService.DiagnoseChannel(["ChannelA-DIMM0", "ChannelA-DIMM1"]);
        Assert.Equal(ChannelStatus.SingleChannelMisconfigured, result.Status);
    }

    [Fact]
    public void DiagnoseChannel_CanalNonIdentifiableNombrePair_LikelyMultiChannel()
    {
        var result = MemoryConfigDiagnosticsService.DiagnoseChannel(["DIMM0", "DIMM1"]);
        Assert.Equal(ChannelStatus.LikelyMultiChannel, result.Status);
    }

    [Fact]
    public void DiagnoseChannel_CanalNonIdentifiableNombreImpair_LikelyImbalanced()
    {
        var result = MemoryConfigDiagnosticsService.DiagnoseChannel(["DIMM0", "DIMM1", "DIMM2"]);
        Assert.Equal(ChannelStatus.LikelyImbalanced, result.Status);
    }

    [Theory]
    [InlineData("ChannelA-DIMM0", 'A')]
    [InlineData("channel b - dimm1", 'B')]
    [InlineData("DIMM0", null)]
    public void ExtractChannelLetter_FormatsVaries(string locator, char? expected)
    {
        Assert.Equal(expected, MemoryConfigDiagnosticsService.ExtractChannelLetter(locator));
    }

    [Fact]
    public void Diagnose_ModulesXmpDesactive_DetecteEtCanalCorrect()
    {
        var modules = new[]
        {
            new MemoryModuleRaw("BANK 0", "ChannelA-DIMM0", 3200, 2133),
            new MemoryModuleRaw("BANK 1", "ChannelB-DIMM0", 3200, 2133)
        };

        var diagnosis = MemoryConfigDiagnosticsService.Diagnose(modules);

        Assert.True(diagnosis.XmpLikelyDisabled);
        Assert.Equal(2, diagnosis.ModuleCount);
        Assert.Equal(ChannelStatus.MultiChannel, diagnosis.Channel.Status);
    }
}

public sealed class PcieLinkServiceTests
{
    [Fact]
    public void ParseNvidiaSmiCsv_GpuBride_IsBridgedVrai()
    {
        const string csv = "NVIDIA GeForce RTX 4060 Ti, 8, 16, 4, 4\n";

        var result = Assert.Single(PcieLinkService.ParseNvidiaSmiCsv(csv));

        Assert.Equal("NVIDIA GeForce RTX 4060 Ti", result.GpuName);
        Assert.True(result.IsBridged);
    }

    [Fact]
    public void ParseNvidiaSmiCsv_LienNominal_IsBridgedFaux()
    {
        const string csv = "NVIDIA GeForce RTX 4060 Ti, 16, 16, 4, 4\n";

        var result = Assert.Single(PcieLinkService.ParseNvidiaSmiCsv(csv));

        Assert.False(result.IsBridged);
    }

    [Fact]
    public void ParseNvidiaSmiCsv_LigneVide_RetourneVide()
    {
        Assert.Empty(PcieLinkService.ParseNvidiaSmiCsv(""));
    }
}

public sealed class SsdWearServiceTests
{
    [Fact]
    public void Label_UsureExposee_AfficheLePourcentage()
    {
        var info = new SsdWearInfo("Samsung 980 Pro", 12, 500);
        Assert.Contains("12%", info.Label);
    }

    [Fact]
    public void Label_UsureNonExposee_MessageExplicite()
    {
        var info = new SsdWearInfo("Disque générique", null, null);
        Assert.Contains("non exposée", info.Label);
    }
}

public sealed class WifiChannelAnalyzerServiceTests
{
    [Fact]
    public void ParseCurrentChannel_FormatFrancais()
    {
        const string output = "Nom              : Wi-Fi\nCanal                  : 6\nSSID               : MonReseau";
        Assert.Equal(6, WifiChannelAnalyzerService.ParseCurrentChannel(output));
    }

    [Fact]
    public void ParseCurrentChannel_FormatAnglais()
    {
        const string output = "Name : Wi-Fi\nChannel : 11\n";
        Assert.Equal(11, WifiChannelAnalyzerService.ParseCurrentChannel(output));
    }

    [Fact]
    public void ParseCurrentChannel_Absent_RetourneNull()
    {
        Assert.Null(WifiChannelAnalyzerService.ParseCurrentChannel("Nom : Wi-Fi\nEtat : connecte"));
    }

    [Fact]
    public void ParseNetworks_ExtraitSsidCanalEtSignal()
    {
        const string output = """
            SSID 1 : Voisin1
                Type de réseau            : Infrastructure
                BSSID 1                   : aa:bb:cc:dd:ee:ff
                     Signal             : 80%
                     Canal              : 1
            SSID 2 : Voisin2
                BSSID 1                   : 11:22:33:44:55:66
                     Signal             : 55%
                     Canal              : 6
            """;

        var networks = WifiChannelAnalyzerService.ParseNetworks(output);

        Assert.Equal(2, networks.Count);
        Assert.Contains(networks, n => n.Ssid == "Voisin1" && n.Channel == 1 && n.SignalPercent == 80);
        Assert.Contains(networks, n => n.Ssid == "Voisin2" && n.Channel == 6 && n.SignalPercent == 55);
    }

    [Fact]
    public void BuildAnalysis_PasDeChannel_RetourneNull()
    {
        var analysis = WifiChannelAnalyzerService.BuildAnalysis(null, []);
        Assert.Null(analysis.CurrentChannel);
    }

    [Fact]
    public void BuildAnalysis_Bande5Ghz_AucuneRecommandation()
    {
        var analysis = WifiChannelAnalyzerService.BuildAnalysis(36, []);
        Assert.True(analysis.Is5GhzOrAbove);
        Assert.Null(analysis.RecommendedChannel);
    }

    [Fact]
    public void BuildAnalysis_CanalEncombre_RecommandeLeMoinsCharge()
    {
        var networks = new[]
        {
            new WifiNetworkObservation("A", 1, 90),
            new WifiNetworkObservation("B", 1, 80),
            new WifiNetworkObservation("C", 1, 70),
            new WifiNetworkObservation("D", 6, 60)
        };

        var analysis = WifiChannelAnalyzerService.BuildAnalysis(1, networks);

        Assert.Equal(11, analysis.RecommendedChannel);
    }

    [Fact]
    public void BuildAnalysis_CanalDejaOptimal_RecommandeLeMemeCanal()
    {
        var analysis = WifiChannelAnalyzerService.BuildAnalysis(6, []);
        Assert.Equal(6, analysis.RecommendedChannel);
    }
}

public sealed class WakeOnLanServiceTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF")]
    [InlineData("aabbccddeeff", "AA:BB:CC:DD:EE:FF")]
    public void NormalizeMac_FormatsVaries(string input, string expected)
    {
        Assert.Equal(expected, WakeOnLanService.NormalizeMac(input));
    }

    [Theory]
    [InlineData("pas une mac")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("")]
    public void NormalizeMac_Invalide_RetourneNull(string input)
    {
        Assert.Null(WakeOnLanService.NormalizeMac(input));
    }

    [Fact]
    public void BuildMagicPacket_102Octets_6xFFPuis16RepetitionsDeLaMac()
    {
        var packet = WakeOnLanService.BuildMagicPacket("AA:BB:CC:DD:EE:FF");

        Assert.Equal(102, packet.Length);
        Assert.All(packet[..6], b => Assert.Equal(0xFF, b));

        var expectedMac = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF };
        for (var i = 0; i < 16; i++)
            Assert.Equal(expectedMac, packet[(6 + i * 6)..(6 + i * 6 + 6)]);
    }
}

public sealed class PublicIpMonitorServiceTests
{
    [Fact]
    public void HasIpChanged_PremiereObservation_Faux()
    {
        Assert.False(PublicIpMonitorService.HasIpChanged(null, "1.2.3.4"));
    }

    [Fact]
    public void HasIpChanged_MemeIp_Faux()
    {
        Assert.False(PublicIpMonitorService.HasIpChanged("1.2.3.4", "1.2.3.4"));
    }

    [Fact]
    public void HasIpChanged_IpDifferente_Vrai()
    {
        Assert.True(PublicIpMonitorService.HasIpChanged("1.2.3.4", "5.6.7.8"));
    }
}

public sealed class AntivirusInventoryServiceTests
{
    [Theory]
    [InlineData(0x000000, false)]
    [InlineData(0x001000, true)]
    [InlineData(0x001100, true)]
    [InlineData(0x000100, false)]
    public void IsAntivirusEnabled_DecodageProductState(int productState, bool expected)
    {
        Assert.Equal(expected, AntivirusInventoryService.IsAntivirusEnabled(productState));
    }

    [Fact]
    public void DetectConflict_UnSeulActif_PasDeConflit()
    {
        var products = new[] { new AntivirusProductInfo("Windows Defender", true) };
        var (hasConflict, enabled) = AntivirusInventoryService.DetectConflict(products);

        Assert.False(hasConflict);
        Assert.Single(enabled);
    }

    [Fact]
    public void DetectConflict_DeuxActifs_Conflit()
    {
        var products = new[]
        {
            new AntivirusProductInfo("Windows Defender", true),
            new AntivirusProductInfo("Avast", true)
        };
        var (hasConflict, enabled) = AntivirusInventoryService.DetectConflict(products);

        Assert.True(hasConflict);
        Assert.Equal(2, enabled.Count);
    }

    [Fact]
    public void DetectConflict_UnActifUnInactif_PasDeConflit()
    {
        var products = new[]
        {
            new AntivirusProductInfo("Windows Defender", false),
            new AntivirusProductInfo("Avast", true)
        };
        var (hasConflict, _) = AntivirusInventoryService.DetectConflict(products);

        Assert.False(hasConflict);
    }
}

public sealed class CrashDiagnosticsServiceCrashLoopTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DetectCrashLoops_SousLeSeuil_RienRemonte()
    {
        var events = new[] { ("Spooler", Now), ("Spooler", Now.AddMinutes(-5)) };
        Assert.Empty(CrashDiagnosticsService.DetectCrashLoops(events, minOccurrences: 3));
    }

    [Fact]
    public void DetectCrashLoops_AuSeuil_Remonte()
    {
        var events = new[] { ("Spooler", Now), ("Spooler", Now.AddMinutes(-10)), ("Spooler", Now.AddMinutes(-20)) };

        var warning = Assert.Single(CrashDiagnosticsService.DetectCrashLoops(events, minOccurrences: 3));

        Assert.Equal("Spooler", warning.ServiceName);
        Assert.Equal(3, warning.Count);
        Assert.Equal(Now, warning.LastAt);
    }

    [Fact]
    public void DetectCrashLoops_ServicesDifferentsIndependants()
    {
        var events = new[]
        {
            ("Spooler", Now), ("Spooler", Now.AddMinutes(-10)), ("Spooler", Now.AddMinutes(-20)),
            ("BITS", Now)
        };

        var warnings = CrashDiagnosticsService.DetectCrashLoops(events, minOccurrences: 3);

        Assert.Single(warnings);
        Assert.Equal("Spooler", warnings[0].ServiceName);
    }

    [Fact]
    public void IsNewBsod_JamaisNotifie_Vrai()
    {
        Assert.True(CrashDiagnosticsService.IsNewBsod(Now, null));
    }

    [Fact]
    public void IsNewBsod_PosterieurAuDernierNotifie_Vrai()
    {
        Assert.True(CrashDiagnosticsService.IsNewBsod(Now, Now.AddDays(-1)));
    }

    [Fact]
    public void IsNewBsod_MemeOuAnterieur_Faux()
    {
        Assert.False(CrashDiagnosticsService.IsNewBsod(Now, Now));
        Assert.False(CrashDiagnosticsService.IsNewBsod(Now.AddDays(-1), Now));
    }
}
