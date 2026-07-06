using System.Text.Json;
using ZiaMonitoring_App.Application;
using ZiaMonitoring_App.Core.Models;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class OptimizationProfileServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ZiaMonitoringTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteProfilesFile(params OptimizationProfile[] profiles)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "import.json");
        File.WriteAllText(path, JsonSerializer.Serialize(profiles));
        return path;
    }

    [Fact]
    public void GetProfiles_ContientLesQuatreProfilsIntegres()
    {
        var service = new OptimizationProfileService(_dir);

        var names = service.GetProfiles().Select(p => p.Name).ToList();

        Assert.Contains("Gaming Max", names);
        Assert.Contains("Travail Silencieux", names);
        Assert.Contains("Streaming", names);
        Assert.Contains("Equilibre", names);
    }

    [Fact]
    public void ImportProfiles_AjouteUnProfilPersonnalise()
    {
        var service = new OptimizationProfileService(_dir);
        var path = WriteProfilesFile(new OptimizationProfile("Mon Profil", "Test", ["Cleanup temp files"]));

        var (imported, skipped) = service.ImportProfiles(path);

        Assert.Equal(1, imported);
        Assert.Equal(0, skipped);
        Assert.Contains(service.GetProfiles(), p => p.Name == "Mon Profil");
    }

    [Fact]
    public void ImportProfiles_ProtegeLesNomsDeProfilsIntegres()
    {
        var service = new OptimizationProfileService(_dir);
        var path = WriteProfilesFile(new OptimizationProfile("Gaming Max", "Tentative d'écrasement", ["Action"]));

        var (imported, skipped) = service.ImportProfiles(path);

        Assert.Equal(0, imported);
        Assert.Equal(1, skipped);
    }

    [Fact]
    public void ImportProfiles_IgnoreLesProfilsInvalides()
    {
        var service = new OptimizationProfileService(_dir);
        var path = WriteProfilesFile(
            new OptimizationProfile("", "Sans nom", ["Action"]),
            new OptimizationProfile("Sans actions", "Vide", []));

        var (imported, skipped) = service.ImportProfiles(path);

        Assert.Equal(0, imported);
        Assert.Equal(2, skipped);
    }

    [Fact]
    public void ImportProfiles_LesProfilsPersonnalisesSurviventAuRedemarrage()
    {
        var path = WriteProfilesFile(new OptimizationProfile("Persistant", "Test", ["Cleanup temp files"]));
        new OptimizationProfileService(_dir).ImportProfiles(path);

        // Nouvelle instance = relecture depuis le disque.
        var reloaded = new OptimizationProfileService(_dir);

        Assert.Contains(reloaded.GetProfiles(), p => p.Name == "Persistant");
    }

    [Fact]
    public void ExportPuisImport_ConserveLesProfils()
    {
        var service = new OptimizationProfileService(_dir);
        var importPath = WriteProfilesFile(new OptimizationProfile("Roundtrip", "Test", ["Action X"]));
        service.ImportProfiles(importPath);

        var exportPath = Path.Combine(_dir, "export.json");
        service.ExportProfiles(exportPath);

        var otherDir = Path.Combine(_dir, "other");
        var other = new OptimizationProfileService(otherDir);
        var (imported, skipped) = other.ImportProfiles(exportPath);

        // Les 4 intégrés sont ignorés (noms réservés), le personnalisé passe.
        Assert.Equal(1, imported);
        Assert.Equal(4, skipped);
        Assert.Contains(other.GetProfiles(), p => p.Name == "Roundtrip");
    }
}

public sealed class UpdateCheckServiceTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("V2.0", "2.0")]
    [InlineData("v1.4.0-beta", "1.4.0")]
    [InlineData("v3", "3.0")]
    public void ParseVersion_GereLesFormatsCourants(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateCheckService.ParseVersion(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("v")]
    public void ParseVersion_TagInvalide_RetourneNull(string tag)
    {
        Assert.Null(UpdateCheckService.ParseVersion(tag));
    }
}
