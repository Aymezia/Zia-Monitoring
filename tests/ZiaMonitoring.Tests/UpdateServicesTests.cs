using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class SelfUpdateServiceTests
{
    [Fact]
    public void IsInstalledBuild_UninsPresent_Vrai()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "unins000.exe"), "");
            Assert.True(SelfUpdateService.IsInstalledBuild(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void IsInstalledBuild_UninsAbsent_Faux()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.False(SelfUpdateService.IsInstalledBuild(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void BuildPortableUpdateScript_ContientLesElementsClefs()
    {
        var script = SelfUpdateService.BuildPortableUpdateScript(
            waitForPid: 4242,
            sourceDir: @"C:\temp\new",
            destDir: @"C:\Program Files\Zia Monitoring",
            exeName: "ZiaMonitoring.App.exe",
            workDir: @"C:\temp\ZiaMonitoringUpdate-abc");

        Assert.Contains("Wait-Process -Id 4242", script);
        Assert.Contains(@"Copy-Item -Path ""C:\temp\new\*"" -Destination ""C:\Program Files\Zia Monitoring""", script);
        Assert.Contains(@"Start-Process -FilePath ""C:\Program Files\Zia Monitoring\ZiaMonitoring.App.exe""", script);
        Assert.Contains(@"Remove-Item -Path ""C:\temp\ZiaMonitoringUpdate-abc""", script);
    }
}
