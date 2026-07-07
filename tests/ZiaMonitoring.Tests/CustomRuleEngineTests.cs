using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class CustomRuleEngineTests
{
    private static SystemSnapshot Snapshot(double cpu = 20, double? cpuTemp = 45, double? gpuTemp = 40,
        double memUsed = 4000, double memTotal = 16000) => new(
        DateTime.Now, cpu, memUsed, memTotal, cpuTemp, gpuTemp, GpuUsagePercent: 10, FanSpeedRpm: 1200,
        VramUsedMb: 1000, VramTotalMb: 8000, DiskIoReadMbps: 0, DiskIoWriteMbps: 0, EstimatedFps: 0,
        PerCoreCpu: [], Network: new NetworkStats(0, 0, 20), ActiveTcpConnections: [], TopNetworkProcesses: [],
        GameServerLatencies: [], TopProcesses: []);

    private static PcProfile Profile(double diskFreeGb = 200) => new(
        "TEST-PC", "Windows 11", "x64", "CPU", "GPU", "Board", "1.0",
        LogicalCores: 16, TotalRamGb: 32, TotalDiskGb: 500, Uptime: TimeSpan.FromHours(2),
        Disks: [new DiskProfile("C:\\", "NTFS", 500, diskFreeGb)], InstalledGames: []);

    [Theory]
    [InlineData(RuleCondition.CpuAbove, 95, 90, true)]
    [InlineData(RuleCondition.CpuAbove, 50, 90, false)]
    public void EvaluateCondition_Cpu_ComparaisonCorrecte(RuleCondition condition, double cpu, double threshold, bool expected)
    {
        var rule = new CustomRule("1", "Test", condition, threshold, 0, RuleAction.Notify);

        Assert.Equal(expected, CustomRuleEngine.EvaluateCondition(rule, Snapshot(cpu: cpu), Profile()));
    }

    [Fact]
    public void EvaluateCondition_RamAbove_CalculeLePourcentage()
    {
        // 15000/16000 = 93.75% > seuil 90%
        var rule = new CustomRule("1", "Test", RuleCondition.RamAbove, 90, 0, RuleAction.Notify);

        Assert.True(CustomRuleEngine.EvaluateCondition(rule, Snapshot(memUsed: 15000, memTotal: 16000), Profile()));
    }

    [Fact]
    public void EvaluateCondition_DiskFreeBelow_DetecteLeDisqueSysteme()
    {
        var rule = new CustomRule("1", "Test", RuleCondition.DiskFreeBelow, 20, 0, RuleAction.Notify);

        Assert.True(CustomRuleEngine.EvaluateCondition(rule, Snapshot(), Profile(diskFreeGb: 5)));
        Assert.False(CustomRuleEngine.EvaluateCondition(rule, Snapshot(), Profile(diskFreeGb: 100)));
    }

    [Fact]
    public void Evaluate_ConditionSoutenueSousLeDelai_NeDeclenchePas()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new CustomRuleEngine(dir);
            engine.AddRule("CPU haut", RuleCondition.CpuAbove, 50, sustainedMinutes: 10, RuleAction.Notify);

            // Un seul cycle : la condition vient de commencer, pas encore soutenue 10 min.
            var triggered = engine.Evaluate(Snapshot(cpu: 95), Profile());

            Assert.Empty(triggered);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Evaluate_ConditionImmediateSansDelai_Declenche()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new CustomRuleEngine(dir);
            engine.AddRule("CPU haut", RuleCondition.CpuAbove, 50, sustainedMinutes: 0, RuleAction.Notify);

            var triggered = engine.Evaluate(Snapshot(cpu: 95), Profile());

            Assert.Single(triggered);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Evaluate_ApresDeclenchement_RespecteLeCooldown()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new CustomRuleEngine(dir);
            engine.AddRule("CPU haut", RuleCondition.CpuAbove, 50, sustainedMinutes: 0, RuleAction.Notify);

            var first = engine.Evaluate(Snapshot(cpu: 95), Profile());
            var second = engine.Evaluate(Snapshot(cpu: 95), Profile());

            Assert.Single(first);
            Assert.Empty(second); // cooldown 15 min
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Evaluate_ConditionRedevientFausse_ReinitialiseLeMinuteur()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new CustomRuleEngine(dir);
            engine.AddRule("CPU haut", RuleCondition.CpuAbove, 50, sustainedMinutes: 10, RuleAction.Notify);

            engine.Evaluate(Snapshot(cpu: 95), Profile()); // condition démarre
            engine.Evaluate(Snapshot(cpu: 10), Profile()); // condition cesse : reset
            var triggered = engine.Evaluate(Snapshot(cpu: 95), Profile()); // repart de zéro

            Assert.Empty(triggered);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void AddRule_PuisRemoveRule_NeLaisseAucuneRegle()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            var engine = new CustomRuleEngine(dir);
            var rule = engine.AddRule("Test", RuleCondition.CpuAbove, 50, 0, RuleAction.Notify);

            Assert.Single(engine.GetRules());

            engine.RemoveRule(rule.Id);

            Assert.Empty(engine.GetRules());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Description_FormateLaRegleLisiblement()
    {
        var rule = new CustomRule("1", "Alerte chaleur", RuleCondition.CpuTempAbove, 85, 5, RuleAction.RunDeepClean);

        Assert.Contains("Alerte chaleur", rule.Description);
        Assert.Contains("85°C", rule.Description);
        Assert.Contains("5 min", rule.Description);
        Assert.Contains("Nettoyage temp", rule.Description);
    }
}
