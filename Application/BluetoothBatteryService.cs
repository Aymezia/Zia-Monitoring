using Windows.Devices.Enumeration;

namespace ZiaMonitoring_App.Application;

public sealed record BluetoothDeviceBattery(string Name, int? BatteryPercent)
{
    public string BatteryLabel => BatteryPercent is { } pct ? $"{pct}%" : "Non exposé";
    public bool IsLow => BatteryPercent is <= 20;
}

/// <summary>
/// Niveau de batterie des périphériques Bluetooth (casque, souris, manette),
/// que Windows n'affiche que dans un sous-menu obscur des Paramètres. Lu via
/// la propriété DEVPKEY_Bluetooth_Battery exposée par l'énumération de
/// périphériques Windows — tous les périphériques ne la renseignent pas.
/// </summary>
public sealed class BluetoothBatteryService
{
    // DEVPKEY_Bluetooth_Battery : niveau de batterie 0-100 exposé par la pile Bluetooth Windows.
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    public async Task<IReadOnlyList<BluetoothDeviceBattery>> ScanAsync()
    {
        var result = new List<BluetoothDeviceBattery>();
        try
        {
            // Périphériques Bluetooth appariés et connectés.
            const string connectedBluetoothSelector =
                "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" AND System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True";

            var devices = await DeviceInformation.FindAllAsync(
                connectedBluetoothSelector, [BatteryProperty]);

            foreach (var device in devices)
            {
                var battery = device.Properties.TryGetValue(BatteryProperty, out var value) && value is byte b ? (int?)b : null;
                result.Add(new BluetoothDeviceBattery(
                    string.IsNullOrWhiteSpace(device.Name) ? "Périphérique Bluetooth" : device.Name, battery));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de la batterie des périphériques Bluetooth impossible", ex);
        }

        return result
            .OrderBy(d => d.BatteryPercent ?? int.MaxValue)
            .ToList();
    }
}
