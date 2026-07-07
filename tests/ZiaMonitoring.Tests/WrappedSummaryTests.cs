using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class WrappedSummaryTests
{
    private static GameSessionService.WrappedSessionRow Row(
        string game, long startedAt, long durationSeconds, double avgFps = 60, double cpuTemp = 70, double gpuTemp = 65)
        => new(game, startedAt, startedAt + durationSeconds, avgFps, cpuTemp, gpuTemp);

    [Fact]
    public void BuildWrappedSummary_AucuneSession_RetourneNull()
    {
        Assert.Null(GameSessionService.BuildWrappedSummary(2026, []));
    }

    [Fact]
    public void BuildWrappedSummary_UneSession_CalculeLesTotaux()
    {
        var sessions = new[] { Row("Valorant", 0, 3600, avgFps: 144, cpuTemp: 75, gpuTemp: 80) };

        var summary = GameSessionService.BuildWrappedSummary(2026, sessions);

        Assert.NotNull(summary);
        Assert.Equal(1, summary!.TotalSessions);
        Assert.Equal(TimeSpan.FromHours(1), summary.TotalPlaytime);
        Assert.Equal("Valorant", summary.MostPlayedGame);
        Assert.Equal(144, summary.AverageFps);
        Assert.Equal(75, summary.MaxCpuTempRecorded);
        Assert.Equal(80, summary.MaxGpuTempRecorded);
    }

    [Fact]
    public void BuildWrappedSummary_PlusieursJeux_IdentifieLePlusJoue()
    {
        var sessions = new[]
        {
            Row("CS2", 0, 3600),
            Row("CS2", 10_000, 3600),
            Row("Valorant", 20_000, 1800)
        };

        var summary = GameSessionService.BuildWrappedSummary(2026, sessions);

        Assert.Equal("CS2", summary!.MostPlayedGame);
        Assert.Equal(TimeSpan.FromHours(2), summary.MostPlayedGameDuration);
        Assert.Equal(3, summary.TotalSessions);
        Assert.Equal(TimeSpan.FromHours(2.5), summary.TotalPlaytime);
    }

    [Fact]
    public void BuildWrappedSummary_IdentifieLaSessionLaPlusLongue()
    {
        var sessions = new[]
        {
            Row("Jeu Court", 0, 600),
            Row("Jeu Long", 1000, 7200)
        };

        var summary = GameSessionService.BuildWrappedSummary(2026, sessions);

        Assert.Equal("Jeu Long", summary!.LongestSessionGame);
        Assert.Equal(TimeSpan.FromHours(2), summary.LongestSessionDuration);
    }

    [Fact]
    public void BuildWrappedSummary_IgnoreLesFpsNonMesures()
    {
        var sessions = new[]
        {
            Row("A", 0, 600, avgFps: 0), // FPS non mesuré (PresentMon absent)
            Row("B", 1000, 600, avgFps: 120)
        };

        var summary = GameSessionService.BuildWrappedSummary(2026, sessions);

        Assert.Equal(120, summary!.AverageFps); // moyenne sur les FPS valides uniquement
    }

    [Fact]
    public void Labels_SansMostPlayedGame_AffichentUnTiret()
    {
        var summary = new WrappedSummary(2026, TimeSpan.Zero, 0, null, TimeSpan.Zero, 0, 0, 0, null, TimeSpan.Zero);

        Assert.Equal("-", summary.MostPlayedLabel);
        Assert.Equal("-", summary.LongestSessionLabel);
        Assert.Equal("N/A", summary.AvgFpsLabel);
    }
}
