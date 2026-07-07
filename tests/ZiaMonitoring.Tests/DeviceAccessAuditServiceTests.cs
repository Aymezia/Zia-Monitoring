using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class DeviceAccessAuditServiceTests
{
    [Fact]
    public void CleanAppName_CheminNonPackaged_NeGardeQueLeNomDuFichier()
    {
        Assert.Equal("Discord.exe",
            DeviceAccessAuditService.CleanAppName(@"C#Users#x#AppData#Local#Discord#app-1.0.9#Discord.exe"));
    }

    [Fact]
    public void CleanAppName_PackageFamilyName_RetourneTelQuel()
    {
        Assert.Equal("Microsoft.WindowsCamera_8wekyb3d8bbwe",
            DeviceAccessAuditService.CleanAppName("Microsoft.WindowsCamera_8wekyb3d8bbwe"));
    }

    [Fact]
    public void ConvertFileTime_ValeurZeroOuNulle_RetourneNull()
    {
        Assert.Null(DeviceAccessAuditService.ConvertFileTime(0L));
        Assert.Null(DeviceAccessAuditService.ConvertFileTime(null));
        Assert.Null(DeviceAccessAuditService.ConvertFileTime("pas un long"));
    }

    [Fact]
    public void ConvertFileTime_ValeurValide_RetourneLaDateAttendue()
    {
        var expected = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc).ToLocalTime();

        var result = DeviceAccessAuditService.ConvertFileTime(expected.ToFileTime());

        Assert.Equal(expected, result);
    }

    [Fact]
    public void DeviceAccessEntry_StopAvantStart_ConsidereCommeEnCours()
    {
        var entry = new DeviceAccessEntry("Webcam", "Discord.exe", DateTime.Now, DateTime.FromFileTime(0));

        Assert.True(entry.IsCurrentlyActive);
        Assert.Equal("En cours d'utilisation", entry.LastUsedLabel);
    }

    [Fact]
    public void DeviceAccessEntry_AccesTermine_AfficheLaDateDeFin()
    {
        var stop = new DateTime(2026, 3, 1, 14, 30, 0);
        var entry = new DeviceAccessEntry("Microphone", "Discord.exe", stop.AddMinutes(-5), stop);

        Assert.False(entry.IsCurrentlyActive);
        Assert.Contains("Dernier accès", entry.LastUsedLabel);
    }
}
