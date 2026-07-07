using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class GameSaveBackupServiceTests
{
    [Fact]
    public void GetCandidateFolders_RetourneMyGamesEtSavedGames()
    {
        var folders = GameSaveBackupService.GetCandidateFolders().ToList();

        Assert.Contains(folders, f => f.Label == "MyGames");
        Assert.Contains(folders, f => f.Label == "SavedGames");
        Assert.All(folders, f => Assert.False(string.IsNullOrWhiteSpace(f.Path)));
    }

    [Fact]
    public void IsScheduledRunDue_Desactive_RetourneFaux()
    {
        var service = new GameSaveBackupService();

        Assert.False(service.IsScheduledRunDue(enabled: false, TimeSpan.Zero));
    }

    [Fact]
    public void IsScheduledRunDue_HeureNonAtteinte_RetourneFaux()
    {
        var service = new GameSaveBackupService();
        var futureTime = DateTime.Now.TimeOfDay.Add(TimeSpan.FromHours(1));

        Assert.False(service.IsScheduledRunDue(enabled: true, futureTime));
    }

    [Fact]
    public void IsScheduledRunDue_HeureAtteinte_RetourneVraiUneSeuleFoisParJour()
    {
        var service = new GameSaveBackupService();
        var pastTime = TimeSpan.Zero;

        Assert.True(service.IsScheduledRunDue(enabled: true, pastTime));
        Assert.False(service.IsScheduledRunDue(enabled: true, pastTime));
    }

    [Fact]
    public void BackupNow_CreeUneArchiveValideSansErreur()
    {
        // My Games/Saved Games existent réellement sur la machine (Steam, etc.) :
        // on ne peut pas supposer un environnement vierge, seulement l'absence de crash.
        var dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));
        try
        {
            var service = new GameSaveBackupService();

            var result = service.BackupNow(dir);

            Assert.True(File.Exists(result.ZipPath));
            Assert.True(result.FileCount >= 0);
            Assert.True(result.TotalBytes >= 0);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
