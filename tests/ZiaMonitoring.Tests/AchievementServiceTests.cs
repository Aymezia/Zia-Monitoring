using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class AchievementServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void GetAchievements_SansProgres_TousVerrouilles()
    {
        var service = new AchievementService(_dir);

        var achievements = service.GetAchievements();

        Assert.All(achievements, a => Assert.False(a.IsUnlocked));
    }

    [Fact]
    public void Increment_AtteintLaCible_DebloqueLeSucces()
    {
        var service = new AchievementService(_dir);

        service.Increment("boosts_applied");

        var achievement = service.GetAchievements().Single(a => a.Id == "booster");
        Assert.True(achievement.IsUnlocked);
        Assert.Equal("Débloqué", achievement.ProgressLabel);
    }

    [Fact]
    public void Increment_SousLaCible_RestVerrouilleAvecProgression()
    {
        var service = new AchievementService(_dir);

        service.Increment("debloat_cleaned", 2);

        var achievement = service.GetAchievements().Single(a => a.Id == "debloater");
        Assert.False(achievement.IsUnlocked);
        Assert.Equal("2/5", achievement.ProgressLabel);
    }

    [Fact]
    public void Increment_PersisteEntreInstances()
    {
        new AchievementService(_dir).Increment("security_scans");

        var reloaded = new AchievementService(_dir).GetAchievements().Single(a => a.Id == "security_guard");

        Assert.True(reloaded.IsUnlocked);
    }

    [Fact]
    public void SetCounter_MemeValeur_NePasEcraserLesAutresCompteurs()
    {
        var service = new AchievementService(_dir);
        service.Increment("boosts_applied");
        service.SetCounter("games_detected", 3);
        service.SetCounter("games_detected", 3);

        var achievements = service.GetAchievements();
        Assert.True(achievements.Single(a => a.Id == "booster").IsUnlocked);
        Assert.Equal(3, achievements.Single(a => a.Id == "librarian").Progress);
    }

    [Fact]
    public void Increment_PremiereUtilisationDunCompteur_IncrementeAussiLesFonctionnalitesDistinctes()
    {
        var service = new AchievementService(_dir);

        service.Increment("boosts_applied");
        service.Increment("security_scans");

        var powerUser = service.GetAchievements().Single(a => a.Id == "power_user");
        Assert.Equal(2, powerUser.Progress);
    }
}
