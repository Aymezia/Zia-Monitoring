using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class GameSessionServiceTests
{
    [Fact]
    public void Percentile_SansEchantillons_RetourneZero()
    {
        Assert.Equal(0, GameSessionService.Percentile([], 0.01));
    }

    [Fact]
    public void Percentile_1PourcentLow_RetourneLaValeurBasse()
    {
        // 100 échantillons : 99 à 120 FPS, 1 à 30 FPS → le 1% low doit capter le 30.
        var samples = Enumerable.Repeat(120d, 99).Append(30d).ToList();

        Assert.Equal(30, GameSessionService.Percentile(samples, 0.01));
    }

    [Fact]
    public void Percentile_MedianeCorrecte()
    {
        var samples = new List<double> { 10, 20, 30, 40, 50 };

        Assert.Equal(30, GameSessionService.Percentile(samples, 0.5));
    }

    [Fact]
    public void StartOfWeek_RetourneUnLundiAMinuit()
    {
        var start = GameSessionService.StartOfWeek();

        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
        Assert.Equal(TimeSpan.Zero, start.TimeOfDay);
        Assert.True(start <= DateTime.Today);
        Assert.True((DateTime.Today - start).TotalDays < 7);
    }

    [Fact]
    public void GetRecentSessions_BaseVide_RetourneListeVide()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new GameSessionService(dir);

            Assert.Empty(service.GetRecentSessions());
            Assert.Equal(TimeSpan.Zero, service.GetWeeklyPlaytime());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
