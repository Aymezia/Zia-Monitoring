using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class SmartTrendServiceTests
{
    private static byte[] BuildSmartTable(params (byte Id, long Raw)[] attributes)
    {
        // Table ATA : 2 octets d'en-tête puis entrées de 12 octets.
        var data = new byte[2 + attributes.Length * 12];
        for (var i = 0; i < attributes.Length; i++)
        {
            var offset = 2 + i * 12;
            data[offset] = attributes[i].Id;
            for (var b = 0; b < 6; b++)
                data[offset + 5 + b] = (byte)(attributes[i].Raw >> (8 * b));
        }
        return data;
    }

    [Fact]
    public void ParseVendorSpecific_LitLesValeursBrutesLittleEndian()
    {
        var data = BuildSmartTable((5, 12), (197, 3), (9, 8760));

        var parsed = SmartTrendService.ParseVendorSpecific(data);

        Assert.Contains(parsed, a => a is { Id: 5, Raw: 12 });
        Assert.Contains(parsed, a => a is { Id: 197, Raw: 3 });
        Assert.Contains(parsed, a => a is { Id: 9, Raw: 8760 });
    }

    [Fact]
    public void ParseVendorSpecific_IgnoreLesEntreesVides()
    {
        var data = BuildSmartTable((0, 999), (5, 1));

        var parsed = SmartTrendService.ParseVendorSpecific(data);

        Assert.Single(parsed);
        Assert.Equal(5, parsed[0].Id);
    }

    [Fact]
    public void ParseVendorSpecific_GrandeValeurSur6Octets()
    {
        var data = BuildSmartTable((241, 0x0000_ABCD_1234_5678 & 0x0000_FFFF_FFFF_FFFF));

        var parsed = SmartTrendService.ParseVendorSpecific(data);

        Assert.Equal(0x0000_ABCD_1234_5678 & 0x0000_FFFF_FFFF_FFFF, parsed[0].Raw);
    }

    [Fact]
    public void CriticalAttributes_ContiennentLesSignauxDeDefaillance()
    {
        Assert.Contains(5, SmartTrendService.CriticalAttributes.Keys);   // secteurs réalloués
        Assert.Contains(197, SmartTrendService.CriticalAttributes.Keys); // secteurs en attente
        Assert.Contains(198, SmartTrendService.CriticalAttributes.Keys); // incorrigibles
    }
}
