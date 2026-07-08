using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class CrashDiagnosticsServiceTests
{
    [Fact]
    public void GroupCrashes_RegroupeParApplicationEtModuleLePlusFrequent()
    {
        var now = new DateTime(2026, 7, 8, 12, 0, 0);
        var crashes = new[]
        {
            new AppCrashInfo(now.AddDays(-1), "game.exe", "nvlddmkm.sys", "0xc0000005", true),
            new AppCrashInfo(now.AddDays(-2), "game.exe", "nvlddmkm.sys", "0xc0000005", true),
            new AppCrashInfo(now.AddDays(-3), "game.exe", "overlay.dll", "0xc0000005", true),
            new AppCrashInfo(now.AddDays(-1), "chrome.exe", "chrome.dll", "0x80000003", false)
        };

        var groups = CrashDiagnosticsService.GroupCrashes(crashes);

        Assert.Equal(2, groups.Count);
        var game = groups[0]; // les jeux connus passent en premier
        Assert.Equal("game.exe", game.AppName);
        Assert.Equal(3, game.Count);
        Assert.Equal("nvlddmkm.sys", game.MostCommonModule);
        Assert.True(game.IsKnownGame);
        Assert.Equal(now.AddDays(-1), game.LastAt);
    }

    [Fact]
    public void GroupCrashes_ListeVide_RetourneVide()
    {
        Assert.Empty(CrashDiagnosticsService.GroupCrashes([]));
    }
}

public sealed class MemoryLeakDetectorTests
{
    private static void Feed(MemoryLeakDetector detector, int pid, string name, double[] memoryMbSeries)
    {
        var start = new DateTime(2026, 7, 8, 8, 0, 0);
        for (var i = 0; i < memoryMbSeries.Length; i++)
            detector.Observe([(pid, name, memoryMbSeries[i])], start.AddMinutes(5 * i));
    }

    [Fact]
    public void Observe_CroissanceMonotoneImportante_SignaleUneFuite()
    {
        var detector = new MemoryLeakDetector();
        // 12 échantillons (1 h), 200 → 640 Mo, croissance continue.
        Feed(detector, 100, "leaky", Enumerable.Range(0, 12).Select(i => 200.0 + i * 40).ToArray());

        var suspect = Assert.Single(detector.Suspects);
        Assert.Equal("leaky", suspect.ProcessName);
        Assert.Equal(200, suspect.StartMb);
        Assert.Equal(640, suspect.CurrentMb);
    }

    [Fact]
    public void Observe_MemoireLibereeEnCoursDeRoute_PasUneFuite()
    {
        var detector = new MemoryLeakDetector();
        // Grosse croissance mais une vraie libération au milieu (chute de 30 %).
        Feed(detector, 100, "cache", [200, 260, 320, 380, 440, 300, 360, 420, 480, 540, 600, 660]);

        Assert.Empty(detector.Suspects);
    }

    [Fact]
    public void Observe_CroissanceTropFaible_PasUneFuite()
    {
        var detector = new MemoryLeakDetector();
        // Monotone mais seulement +110 Mo au total (< seuil de 300).
        Feed(detector, 100, "stable", Enumerable.Range(0, 12).Select(i => 200.0 + i * 10).ToArray());

        Assert.Empty(detector.Suspects);
    }

    [Fact]
    public void Observe_PasAssezDEchantillons_PasDeVerdict()
    {
        var detector = new MemoryLeakDetector();
        Feed(detector, 100, "young", [200, 400, 600, 800]);

        Assert.Empty(detector.Suspects);
    }

    [Fact]
    public void Observe_ProcessTermine_EstOublie()
    {
        var detector = new MemoryLeakDetector();
        Feed(detector, 100, "leaky", Enumerable.Range(0, 12).Select(i => 200.0 + i * 40).ToArray());
        Assert.Single(detector.Suspects);

        // Cycle suivant sans ce PID : le process est mort, plus de suspect.
        detector.Observe([(200, "other", 100)], new DateTime(2026, 7, 8, 10, 0, 0));

        Assert.Empty(detector.Suspects);
    }
}

public sealed class ThermalDriftTests
{
    private const long Today = 20_000;

    private static List<(long Day, int Bucket, double AvgTemp, int Samples)> BuildRows(
        double baselineTemp, double recentTemp, int bucket = 1)
    {
        var rows = new List<(long, int, double, int)>();
        for (var i = 25; i <= 30; i++) rows.Add((Today - i, bucket, baselineTemp, 100));
        for (var i = 1; i <= 5; i++) rows.Add((Today - i, bucket, recentTemp, 100));
        return rows;
    }

    [Fact]
    public void ComputeThermalDrift_DerivDe6Degres_Alerte()
    {
        var warnings = MetricsHistoryService.ComputeThermalDrift(BuildRows(62, 68), Today);

        var warning = Assert.Single(warnings);
        Assert.Contains("+6", warning);
        Assert.Contains("charge moyenne", warning);
    }

    [Fact]
    public void ComputeThermalDrift_DerivFaible_PasDAlerte()
    {
        Assert.Empty(MetricsHistoryService.ComputeThermalDrift(BuildRows(62, 64), Today));
    }

    [Fact]
    public void ComputeThermalDrift_PasAssezDHistoriqueAncien_PasDAlerte()
    {
        // Seulement des données récentes : aucune référence à 21+ jours.
        var rows = new List<(long, int, double, int)>();
        for (var i = 1; i <= 7; i++) rows.Add((Today - i, 1, 70.0, 100));

        Assert.Empty(MetricsHistoryService.ComputeThermalDrift(rows, Today));
    }

    [Fact]
    public void ComputeThermalDrift_TemperatureEnBaisse_PasDAlerte()
    {
        Assert.Empty(MetricsHistoryService.ComputeThermalDrift(BuildRows(70, 62), Today));
    }
}
