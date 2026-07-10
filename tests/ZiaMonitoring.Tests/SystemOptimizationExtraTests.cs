using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class PendingRebootServiceTests
{
    [Fact]
    public void Evaluate_AucunIndicateur_PasDeReboot()
    {
        var status = PendingRebootService.Evaluate(false, false, false, false, TimeSpan.FromHours(1));
        Assert.False(status.RebootRequired);
        Assert.Empty(status.Reasons);
        Assert.Contains("Aucun", status.Label);
    }

    [Fact]
    public void Evaluate_CbsSeul_RebootRequis()
    {
        var status = PendingRebootService.Evaluate(cbs: true, false, false, false, TimeSpan.FromHours(3));
        Assert.True(status.RebootRequired);
        Assert.Single(status.Reasons);
    }

    [Fact]
    public void Evaluate_WindowsUpdate_RebootRequis()
    {
        var status = PendingRebootService.Evaluate(false, windowsUpdate: true, false, false, TimeSpan.FromDays(4));
        Assert.True(status.RebootRequired);
        Assert.Contains("Windows Update", status.Reasons);
    }

    [Fact]
    public void Evaluate_PendingRenameSeul_SignalFaibleSansRebootRequis()
    {
        var status = PendingRebootService.Evaluate(false, false, false, pendingRename: true, TimeSpan.FromHours(1));
        Assert.False(status.RebootRequired);
        Assert.Single(status.SoftIndicators);
        Assert.Contains("attente", status.Label);
    }

    [Fact]
    public void Evaluate_PlusieursIndicateursForts_TousListes()
    {
        var status = PendingRebootService.Evaluate(true, true, true, false, TimeSpan.FromHours(1));
        Assert.True(status.RebootRequired);
        Assert.Equal(3, status.Reasons.Count);
    }
}

public sealed class PcAuditPendingRebootTests
{
    [Fact]
    public void BuildPendingRebootFindings_PasDeReboot_Vide()
    {
        var status = new PendingRebootStatus(false, [], [], TimeSpan.FromHours(1));
        Assert.Empty(PcAuditService.BuildPendingRebootFindings(status));
    }

    [Fact]
    public void BuildPendingRebootFindings_RebootRecent_Info()
    {
        var status = new PendingRebootStatus(true, ["Windows Update"], [], TimeSpan.FromHours(6));
        var finding = Assert.Single(PcAuditService.BuildPendingRebootFindings(status));
        Assert.Equal(AuditSeverity.Info, finding.Severity);
    }

    [Fact]
    public void BuildPendingRebootFindings_RebootAncien_Warning()
    {
        var status = new PendingRebootStatus(true, ["Windows Update"], [], TimeSpan.FromDays(3));
        var finding = Assert.Single(PcAuditService.BuildPendingRebootFindings(status));
        Assert.Equal(AuditSeverity.Warning, finding.Severity);
    }
}

public sealed class StorageSenseConfigTests
{
    [Theory]
    [InlineData(0, "faible")]
    [InlineData(1, "jour")]
    [InlineData(7, "semaine")]
    [InlineData(30, "mois")]
    public void CadenceLabel_TexteLisible(int cadence, string fragment)
    {
        var config = new StorageSenseConfig(true, cadence, true, 0, 0);
        Assert.Contains(fragment, config.CadenceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, "Jamais")]
    [InlineData(1, "1 jour")]
    [InlineData(30, "30 jours")]
    public void RecycleBinLabel_TexteLisible(int days, string expected)
    {
        var config = new StorageSenseConfig(true, 0, true, days, 0);
        Assert.Equal(expected, config.RecycleBinLabel);
    }
}

public sealed class NetworkAdapterPowerServiceTests
{
    [Theory]
    [InlineData(0, true)]      // aucun bit : Windows peut éteindre
    [InlineData(0x100, false)] // bit posé : extinction interdite
    [InlineData(0x118, false)] // bit 0x100 présent parmi d'autres
    [InlineData(0x18, true)]   // bit 0x100 absent
    public void IsPowerOffAllowed_InterpreteLeBit(int pnpCapabilities, bool expected)
    {
        Assert.Equal(expected, NetworkAdapterPowerService.IsPowerOffAllowed(pnpCapabilities));
    }

    [Theory]
    [InlineData("Realtek PCIe GbE Family Controller", false)]
    [InlineData("Intel(R) Wi-Fi 6 AX200", false)]
    [InlineData("WAN Miniport (IP)", true)]
    [InlineData("Microsoft Wi-Fi Direct Virtual Adapter", true)]
    [InlineData("VMware Virtual Ethernet Adapter", true)]
    public void IsVirtual_ReconnaitLesAdaptateursVirtuels(string desc, bool expected)
    {
        Assert.Equal(expected, NetworkAdapterPowerService.IsVirtual(desc));
    }
}

public sealed class NetworkUsageHistoryServiceTests
{
    [Fact]
    public void EstimateBytes_KbParSecondeVersOctets()
    {
        // 100 KB/s pendant 10 s = 1000 Ko = 1 024 000 octets.
        Assert.Equal(100 * 1024.0 * 10, NetworkUsageHistoryService.EstimateBytes(100, 10));
    }

    [Fact]
    public void EstimateBytes_ZeroDebit_Zero()
    {
        Assert.Equal(0, NetworkUsageHistoryService.EstimateBytes(0, 60));
    }

    [Fact]
    public void NetworkConsumer_Label_BasculeGo()
    {
        Assert.Contains("Mo", new NetworkConsumer("app", 500).Label);
        Assert.Contains("Go", new NetworkConsumer("app", 2048).Label);
    }
}

public sealed class DpcLatencyProbeTests
{
    [Theory]
    [InlineData(500, "Excellent")]
    [InlineData(1500, "Correct")]
    [InlineData(3000, "Limite")]
    [InlineData(8000, "Problématique")]
    public void Classify_SelonLaLatenceMax(double maxUs, string fragment)
    {
        Assert.Contains(fragment, LatencyProbeResult.Classify(maxUs));
    }

    [Fact]
    public void LatencyProbeResult_Label_ContientLesDeuxMesures()
    {
        var result = new LatencyProbeResult(1234, 200);
        Assert.Contains("1234", result.Label);
        Assert.Contains("200", result.Label);
    }
}

public sealed class WindowsTweaksHvciTests
{
    [Theory]
    [InlineData(0, TweakState.Optimized)]  // HVCI off = optimisé pour le jeu
    [InlineData(1, TweakState.Default)]    // HVCI on = défaut Windows récent
    [InlineData(null, TweakState.Unknown)]
    public void HvciState_MappeLesValeurs(int? value, TweakState expected)
    {
        Assert.Equal(expected, WindowsTweaksService.HvciState(value));
    }

    [Fact]
    public void HvciLabel_TexteLisible()
    {
        Assert.Equal("Activée", WindowsTweaksService.HvciLabel(1));
        Assert.Equal("Désactivée", WindowsTweaksService.HvciLabel(0));
    }
}
