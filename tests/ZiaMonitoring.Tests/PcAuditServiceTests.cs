using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class PcAuditServiceTests
{
    private static SystemSnapshot MakeSnapshot(
        double cpu = 40, double memUsed = 4000, double memTotal = 16000,
        double? cpuTemp = 60, double? gpuTemp = 60) => new(
        Timestamp: DateTime.Now,
        CpuPercent: cpu,
        MemoryUsedMb: memUsed,
        MemoryTotalMb: memTotal,
        CpuTemperatureC: cpuTemp,
        GpuTemperatureC: gpuTemp,
        GpuUsagePercent: 20,
        FanSpeedRpm: 1200,
        VramUsedMb: 500,
        VramTotalMb: 8000,
        DiskIoReadMbps: 1,
        DiskIoWriteMbps: 1,
        EstimatedFps: 0,
        PerCoreCpu: [],
        Network: new NetworkStats(0, 0, 10),
        ActiveTcpConnections: [],
        TopNetworkProcesses: [],
        GameServerLatencies: [],
        TopProcesses: []);

    private static PcProfile MakeProfile(double totalRamGb = 32, IReadOnlyList<DiskProfile>? disks = null) => new(
        MachineName: "PC-TEST",
        OperatingSystem: "Windows 11",
        Architecture: "X64",
        CpuModel: "Ryzen 5 5500",
        GpuModel: "RTX 4060 Ti",
        Motherboard: "Test Board",
        BiosVersion: "1.0",
        LogicalCores: 12,
        TotalRamGb: totalRamGb,
        TotalDiskGb: 1000,
        Uptime: TimeSpan.FromHours(5),
        Disks: disks ?? [new DiskProfile("C:\\", "NTFS", 500, 100)],
        InstalledGames: []);

    [Fact]
    public void BuildRealtimeFindings_CpuEtRamCritiques()
    {
        var snapshot = MakeSnapshot(cpu: 90, memUsed: 15000, memTotal: 16000);
        var findings = PcAuditService.BuildRealtimeFindings(snapshot, MakeProfile());

        Assert.Contains(findings, f => f.Title == "Charge CPU élevée");
        Assert.Contains(findings, f => f.Title == "Mémoire vive saturée");
    }

    [Fact]
    public void BuildRealtimeFindings_SurchauffeCpuCritique()
    {
        var findings = PcAuditService.BuildRealtimeFindings(MakeSnapshot(cpuTemp: 95), MakeProfile());
        var finding = Assert.Single(findings, f => f.Title == "Surchauffe CPU");
        Assert.Equal(AuditSeverity.Critical, finding.Severity);
    }

    [Fact]
    public void BuildRealtimeFindings_TemperatureCpuVigilance()
    {
        var findings = PcAuditService.BuildRealtimeFindings(MakeSnapshot(cpuTemp: 82), MakeProfile());
        var finding = Assert.Single(findings, f => f.Title == "Températures CPU élevées");
        Assert.Equal(AuditSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void BuildRealtimeFindings_DisqueSystemePresquePlein()
    {
        var disks = new List<DiskProfile> { new("C:\\", "NTFS", 500, 20) }; // 4%
        var findings = PcAuditService.BuildRealtimeFindings(MakeSnapshot(), MakeProfile(disks: disks));

        var finding = Assert.Single(findings, f => f.Title.Contains("Disque système"));
        Assert.Equal(AuditSeverity.Critical, finding.Severity);
    }

    [Fact]
    public void BuildRealtimeFindings_RamSousLeConfort()
    {
        var findings = PcAuditService.BuildRealtimeFindings(MakeSnapshot(), MakeProfile(totalRamGb: 8));
        Assert.Contains(findings, f => f.Title.Contains("RAM sous"));
    }

    [Fact]
    public void BuildRealtimeFindings_ConfigurationSaine_AucunConstat()
    {
        var findings = PcAuditService.BuildRealtimeFindings(MakeSnapshot(), MakeProfile());
        Assert.Empty(findings);
    }

    private static SecurityReport MakeSecurityReport(
        bool firewall = true, bool uac = true,
        IReadOnlyList<string>? smart = null, IReadOnlyList<string>? malicious = null,
        IReadOnlyList<string>? keylogger = null, IReadOnlyList<string>? suspicious = null,
        IReadOnlyList<ObsoleteDriverInfo>? drivers = null,
        IReadOnlyList<AntivirusProductInfo>? antivirus = null, bool avConflict = false) => new(
        DateTime.Now, 0, firewall, uac, [],
        suspicious ?? [], drivers ?? [], smart ?? [], malicious ?? [], keylogger ?? [],
        antivirus ?? [], avConflict);

    [Fact]
    public void BuildSecurityFindings_ParefeuEtUacDesactives_Critiques()
    {
        var findings = PcAuditService.BuildSecurityFindings(MakeSecurityReport(firewall: false, uac: false));

        Assert.Contains(findings, f => f.Title.Contains("Pare-feu") && f.Severity == AuditSeverity.Critical);
        Assert.Contains(findings, f => f.Title.Contains("UAC") && f.Severity == AuditSeverity.Critical);
    }

    [Fact]
    public void BuildSecurityFindings_SmartMaliciousKeylogger_Critiques()
    {
        var findings = PcAuditService.BuildSecurityFindings(MakeSecurityReport(
            smart: ["disque X"], malicious: ["proc.exe"], keylogger: ["hook suspect"]));

        Assert.Contains(findings, f => f.Title.Contains("S.M.A.R.T"));
        Assert.Contains(findings, f => f.Title.Contains("malveillante"));
        Assert.Contains(findings, f => f.Title.Contains("clavier"));
        Assert.All(findings, f => Assert.Equal(AuditSeverity.Critical, f.Severity));
    }

    [Fact]
    public void BuildSecurityFindings_ConflitAntivirus_Warning()
    {
        var findings = PcAuditService.BuildSecurityFindings(MakeSecurityReport(
            antivirus: [new AntivirusProductInfo("Defender", true), new AntivirusProductInfo("Avast", true)],
            avConflict: true));

        var finding = Assert.Single(findings, f => f.Title.Contains("antivirus"));
        Assert.Equal(AuditSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void BuildSecurityFindings_ToutEstOk_AucunConstat()
    {
        Assert.Empty(PcAuditService.BuildSecurityFindings(MakeSecurityReport()));
    }

    [Fact]
    public void BuildPrivacyFindings_ChecksExposes_UnConstatAgrege()
    {
        var checks = new[]
        {
            new PrivacyCheck("ad-id", "Identifiant publicitaire", "...", false),
            new PrivacyCheck("history", "Historique", "...", true)
        };

        var finding = Assert.Single(PcAuditService.BuildPrivacyFindings(checks));
        Assert.Contains("Identifiant publicitaire", finding.Detail);
        Assert.DoesNotContain("Historique", finding.Detail);
    }

    [Fact]
    public void BuildDebloatFindings_TelemetrieEtTachesActives()
    {
        var items = new[]
        {
            new DebloatItem(DebloatCategory.Telemetry, "DiagTrack", "DiagTrack", "...", false),
            new DebloatItem(DebloatCategory.ScheduledTask, "task1", "Task1", "...", false),
            new DebloatItem(DebloatCategory.BloatwareApp, "app1", "App1", "...", false)
        };

        var findings = PcAuditService.BuildDebloatFindings(items);

        Assert.Contains(findings, f => f.Title.Contains("Télémétrie"));
        Assert.Contains(findings, f => f.Title.Contains("Tâches planifiées"));
        Assert.DoesNotContain(findings, f => f.Title.Contains("App1"));
    }

    [Fact]
    public void BuildStartupFindings_EntreesLourdesActives()
    {
        var entries = new[]
        {
            new StartupEntry("Discord", "discord.exe", "Utilisateur (HKCU)", true, "élevé"),
            new StartupEntry("Notepad", "notepad.exe", "Utilisateur (HKCU)", false, "faible (estimé)")
        };

        var finding = Assert.Single(PcAuditService.BuildStartupFindings(entries));
        Assert.Contains("1 application", finding.Detail);
    }

    [Fact]
    public void BuildStabilityFindings_TousLesSignaux()
    {
        var crashes = new[] { new AppCrashGroup("game.exe", 5, DateTime.Now, "nvwgf2umx.dll", true) };
        var whea = new WheaSummary(3, DateTime.Now);
        var bsod = new BsodInfo(DateTime.Now.AddDays(-2), "CRITICAL_PROCESS_DIED");
        var loops = new[] { new ServiceCrashLoopWarning("Spooler", 4, DateTime.Now) };
        var leaks = new[] { new LeakSuspect("app.exe", 1234, 200, 900, TimeSpan.FromHours(2)) };
        var drift = new[] { "CPU : +6°C vs baseline" };

        var findings = PcAuditService.BuildStabilityFindings(crashes, whea, bsod, loops, leaks, drift);

        Assert.Contains(findings, f => f.Title.Contains("Crashs"));
        Assert.Contains(findings, f => f.Title.Contains("WHEA"));
        Assert.Contains(findings, f => f.Title.Contains("Écran bleu") && f.Severity == AuditSeverity.Critical);
        Assert.Contains(findings, f => f.Title.Contains("Service Windows instable"));
        Assert.Contains(findings, f => f.Title.Contains("Fuite mémoire"));
        Assert.Contains(findings, f => f.Title.Contains("Dérive thermique"));
    }

    [Fact]
    public void BuildStabilityFindings_BsodAncien_PasDeConstat()
    {
        var bsod = new BsodInfo(DateTime.Now.AddDays(-30), "OLD_ERROR");
        var findings = PcAuditService.BuildStabilityFindings([], new WheaSummary(0, null), bsod, [], [], []);

        Assert.DoesNotContain(findings, f => f.Title.Contains("Écran bleu"));
    }

    [Fact]
    public void BuildHardwareFindings_XmpEtCanalEtPcieEtUsure()
    {
        var memory = new MemoryConfigDiagnosis(1, 3200, 2133, true,
            new ChannelDiagnosis(ChannelStatus.SingleChannel, 1, "mono-canal"));
        var pcie = new[] { new PcieLinkInfo("RTX 4060 Ti", 8, 16, 4, 4) };
        var ssd = new[] { new SsdWearInfo("SSD 1", 95, 1000), new SsdWearInfo("SSD 2", 50, 500) };
        var batteries = new[] { new BatteryHealthInfo("Batterie", 54000, 30000, 400) };

        var findings = PcAuditService.BuildHardwareFindings(memory, pcie, ssd, batteries);

        Assert.Contains(findings, f => f.Title.Contains("XMP"));
        Assert.Contains(findings, f => f.Title.Contains("mono-canal"));
        Assert.Contains(findings, f => f.Title.Contains("PCIe") && f.Severity == AuditSeverity.Warning);
        Assert.Contains(findings, f => f.Title.Contains("Usure SSD") && f.Severity == AuditSeverity.Critical);
        Assert.DoesNotContain(findings, f => f.Detail.Contains("SSD 2"));
        Assert.Contains(findings, f => f.Title.Contains("Batterie"));
    }

    [Fact]
    public void BuildUpdateFindings_MiseAJourRecente_AucunConstat()
    {
        var updates = new[] { new InstalledUpdateInfo("KB1", "desc", DateTime.Now.AddDays(-5)) };
        Assert.Empty(PcAuditService.BuildUpdateFindings(updates));
    }

    [Fact]
    public void BuildUpdateFindings_MiseAJourAncienne_Constat()
    {
        var updates = new[] { new InstalledUpdateInfo("KB1", "desc", DateTime.Now.AddDays(-90)) };
        Assert.Single(PcAuditService.BuildUpdateFindings(updates));
    }

    [Theory]
    [InlineData(0, 0, 0, 100)]
    [InlineData(1, 0, 0, 86)]
    [InlineData(0, 1, 0, 94)]
    [InlineData(0, 0, 1, 98)]
    [InlineData(10, 0, 0, 0)]
    public void ComputeScore_PonderationParSeverite(int critical, int warning, int info, int expected)
    {
        var findings = new List<PcAuditFinding>();
        findings.AddRange(Enumerable.Repeat(new PcAuditFinding(AuditSeverity.Critical, AuditCategory.Securite, "c", "d", "r"), critical));
        findings.AddRange(Enumerable.Repeat(new PcAuditFinding(AuditSeverity.Warning, AuditCategory.Securite, "w", "d", "r"), warning));
        findings.AddRange(Enumerable.Repeat(new PcAuditFinding(AuditSeverity.Info, AuditCategory.Securite, "i", "d", "r"), info));

        Assert.Equal(expected, PcAuditService.ComputeScore(findings));
    }
}
