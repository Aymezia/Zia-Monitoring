using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class WindowsTweaksServiceTests
{
    [Theory]
    [InlineData(2, TweakState.Optimized)]
    [InlineData(1, TweakState.Default)]
    [InlineData(null, TweakState.Unknown)]
    public void HagsState_MappeLesValeurs(int? value, TweakState expected)
    {
        Assert.Equal(expected, WindowsTweaksService.HagsState(value));
    }

    [Fact]
    public void NetworkThrottlingState_DesactiveEstOptimise()
    {
        Assert.Equal(TweakState.Optimized, WindowsTweaksService.NetworkThrottlingState(unchecked((int)0xFFFFFFFF)));
        Assert.Equal(TweakState.Default, WindowsTweaksService.NetworkThrottlingState(10));
        Assert.Equal(TweakState.Unknown, WindowsTweaksService.NetworkThrottlingState(null));
    }

    [Theory]
    [InlineData(0, TweakState.Optimized)]
    [InlineData(10, TweakState.Optimized)]
    [InlineData(20, TweakState.Default)]
    [InlineData(null, TweakState.Unknown)]
    public void SystemResponsivenessState_MappeLesValeurs(int? value, TweakState expected)
    {
        Assert.Equal(expected, WindowsTweaksService.SystemResponsivenessState(value));
    }

    [Theory]
    [InlineData(4, TweakState.Optimized)]  // désactivé
    [InlineData(2, TweakState.Default)]    // automatique
    [InlineData(3, TweakState.Default)]    // manuel
    [InlineData(null, TweakState.Unknown)]
    public void SysMainState_MappeLesValeurs(int? startType, TweakState expected)
    {
        Assert.Equal(expected, WindowsTweaksService.SysMainState(startType));
    }

    [Theory]
    [InlineData(0, TweakState.Optimized)]  // fast startup désactivé
    [InlineData(1, TweakState.Default)]    // activé
    [InlineData(null, TweakState.Unknown)]
    public void FastStartupState_MappeLesValeurs(int? value, TweakState expected)
    {
        Assert.Equal(expected, WindowsTweaksService.FastStartupState(value));
    }

    [Theory]
    [InlineData(2, TweakState.Optimized)]  // performances
    [InlineData(1, TweakState.Default)]    // apparence
    [InlineData(null, TweakState.Unknown)]
    public void VisualEffectsState_MappeLesValeurs(int? value, TweakState expected)
    {
        Assert.Equal(expected, WindowsTweaksService.VisualEffectsState(value));
    }

    [Fact]
    public void NagleState_MappeLeBooleen()
    {
        Assert.Equal(TweakState.Optimized, WindowsTweaksService.NagleState(disabled: true));
        Assert.Equal(TweakState.Default, WindowsTweaksService.NagleState(disabled: false));
    }

    [Fact]
    public void NetworkThrottlingLabel_TexteExplicite()
    {
        Assert.Contains("optimisé", WindowsTweaksService.NetworkThrottlingLabel(unchecked((int)0xFFFFFFFF)));
        Assert.Contains("défaut", WindowsTweaksService.NetworkThrottlingLabel(10));
    }
}

public sealed class AdvancedCleanupServiceTests
{
    [Theory]
    [InlineData(".exe", true)]
    [InlineData(".msi", true)]
    [InlineData(".EXE", true)]
    [InlineData(".zip", false)]
    [InlineData(".txt", false)]
    public void IsInstaller_ReconnaitLesInstalleurs(string extension, bool expected)
    {
        Assert.Equal(expected, AdvancedCleanupService.IsInstaller(extension));
    }

    [Fact]
    public void RestorePointsUsage_Label_AucunPoint()
    {
        Assert.Contains("Aucun", new RestorePointsUsage(0, 0).Label);
    }

    [Fact]
    public void RestorePointsUsage_Label_AvecPoints()
    {
        var label = new RestorePointsUsage(5.3, 3).Label;
        Assert.Contains("3", label);
        Assert.Contains("5", label);
    }

    [Fact]
    public void CleanupTarget_SizeLabel_BasculeGoAuDelaDe1024Mo()
    {
        Assert.Equal("512 Mo", new CleanupTarget("x", "n", "d", 512, true).SizeLabel);
        Assert.Equal("2,0 Go", new CleanupTarget("x", "n", "d", 2048, true).SizeLabel);
    }
}

public sealed class SystemHygieneServiceTests
{
    [Theory]
    [InlineData("New", true)]
    [InlineData("Sharing", true)]
    [InlineData("Open With", true)]
    [InlineData("7-Zip", false)]
    [InlineData("NVIDIA CPL Context Menu", false)]
    public void IsBuiltIn_ExclutLesGestionnairesWindows(string name, bool expected)
    {
        Assert.Equal(expected, SystemHygieneService.IsBuiltIn(name));
    }

    [Fact]
    public void PrettifyName_GardeLeNomLisible()
    {
        Assert.Equal("7-Zip", SystemHygieneService.PrettifyName("7-Zip"));
    }
}

public sealed class SteamLibraryServiceTests
{
    [Fact]
    public void ParseLibraryPaths_ExtraitLesCheminsDesEchappes()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                }
                "1"
                {
                    "path"		"D:\\SteamLibrary"
                }
            }
            """;

        var paths = SteamLibraryService.ParseLibraryPaths(vdf);

        Assert.Equal(2, paths.Count);
        Assert.Contains(@"C:\Program Files (x86)\Steam", paths);
        Assert.Contains(@"D:\SteamLibrary", paths);
    }

    [Fact]
    public void ParseLibraryPaths_VdfVide_RetourneVide()
    {
        Assert.Empty(SteamLibraryService.ParseLibraryPaths(""));
    }

    [Fact]
    public void ParseAppManifest_ExtraitNomEtTaille()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"570"
                "name"		"Dota 2"
                "SizeOnDisk"		"32212254720"
            }
            """;

        var game = SteamLibraryService.ParseAppManifest(acf);

        Assert.NotNull(game);
        Assert.Equal("Dota 2", game!.Name);
        Assert.Equal(30, game.SizeGb, precision: 0);
    }

    [Fact]
    public void ParseAppManifest_SansNom_RetourneNull()
    {
        Assert.Null(SteamLibraryService.ParseAppManifest("\"AppState\" { \"appid\" \"1\" }"));
    }

    [Fact]
    public void SteamLibrary_IsOnHdd_SelonLeType()
    {
        var hdd = new SteamLibrary("D:\\", "D:", "HDD", [new SteamGame("Jeu", 40)]);
        var ssd = new SteamLibrary("C:\\", "C:", "SSD", [new SteamGame("Jeu", 40)]);

        Assert.True(hdd.IsOnHdd);
        Assert.False(ssd.IsOnHdd);
        Assert.Contains("déplacer", hdd.Advice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SteamLibrary_TotalSizeGb_SommeLesJeux()
    {
        var lib = new SteamLibrary("C:\\", "C:", "SSD", [new SteamGame("A", 10), new SteamGame("B", 25)]);
        Assert.Equal(35, lib.TotalSizeGb);
    }
}
