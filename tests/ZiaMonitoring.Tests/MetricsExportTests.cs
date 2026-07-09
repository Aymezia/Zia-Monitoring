using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class MetricsExportTests
{
    [Fact]
    public void BuildCsv_ListeVide_ContientSeulementLEntete()
    {
        var csv = MetricsHistoryService.BuildCsv([]);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("Timestamp,CpuPercent,MemoryUsedMb,MemoryTotalMb,CpuTemperatureC,GpuTemperatureC,GpuUsagePercent", lines[0]);
    }

    [Fact]
    public void BuildCsv_EchantillonComplet_FormateToutesLesColonnes()
    {
        var row = new MetricSampleRow(
            new DateTime(2026, 3, 1, 14, 30, 0), 55.5, 8000, 16000, 62.3, 70.1, 45.0);

        var csv = MetricsHistoryService.BuildCsv([row]);

        Assert.Contains("2026-03-01T14:30:00,55.5,8000,16000,62.3,70.1,45", csv);
    }

    [Fact]
    public void BuildCsv_TemperaturesNulles_LaisseLesColonnesVides()
    {
        var row = new MetricSampleRow(
            new DateTime(2026, 3, 1, 0, 0, 0), 10, 4000, 16000, null, null, null);

        var csv = MetricsHistoryService.BuildCsv([row]);

        Assert.Contains("2026-03-01T00:00:00,10,4000,16000,,,", csv);
    }

    [Fact]
    public void BuildCsv_UtiliseLePointCommeSeparateurDecimal()
    {
        // Sensible aux paramètres régionaux (virgule) si CultureInfo n'est pas forcée en Invariant.
        var row = new MetricSampleRow(DateTime.Now, 12.34, 0, 0, null, null, null);

        var csv = MetricsHistoryService.BuildCsv([row]);

        Assert.Contains("12.34", csv);
        Assert.DoesNotContain("12,34", csv);
    }

    [Fact]
    public void BuildWarrantyCsv_ListesVides_ContientSeulementLEntete()
    {
        var csv = MetricsHistoryService.BuildWarrantyCsv([], []);

        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Equal("Date,Categorie,Detail,Valeur", lines[0]);
    }

    [Fact]
    public void BuildWarrantyCsv_TemperatureEtUsureDisque_LignesFormatees()
    {
        var today = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
        var thermalRows = new[] { (Day: today, Bucket: 1, AvgTemp: 68.4, Samples: 12) };
        var diskRows = new[] { (Day: today, DiskName: "Samsung 980 Pro", WearPercent: 12) };

        var csv = MetricsHistoryService.BuildWarrantyCsv(thermalRows, diskRows);

        Assert.Contains("Temperature CPU (C),charge moyenne (30-70 %),68.4", csv);
        Assert.Contains("Usure disque (%),Samsung 980 Pro,12", csv);
    }

    [Fact]
    public void BuildWarrantyCsv_NomDeDisqueAvecVirgule_EchappeEnCsv()
    {
        var today = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
        var diskRows = new[] { (Day: today, DiskName: "Disque, avec virgule", WearPercent: 5) };

        var csv = MetricsHistoryService.BuildWarrantyCsv([], diskRows);

        Assert.Contains("\"Disque, avec virgule\"", csv);
    }
}
