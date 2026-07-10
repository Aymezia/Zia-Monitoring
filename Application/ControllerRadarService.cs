using Windows.Gaming.Input;

namespace ZiaMonitoring_App.Application;

public sealed record ControllerInfo(string Name, string Kind, int? BatteryPercent)
{
    public string BatteryLabel => BatteryPercent is { } pct ? $"{pct}%" : "N/A";
}

/// <summary>Position instantanée des sticks, chaque axe dans [-1, 1] (0 = centre).</summary>
public sealed record StickReading(double LeftX, double LeftY, double RightX, double RightY);

/// <summary>
/// Détecte les manettes connectées via Windows.Gaming.Input : Gamepad
/// couvre XInput (Xbox et compatibles), RawGameController couvre en plus
/// le HID générique (DualSense/DualShock via Bluetooth ou USB, etc.). La
/// batterie n'est disponible que si le pilote/manette l'expose.
/// </summary>
public static class ControllerRadarService
{
    public static IReadOnlyList<ControllerInfo> Scan()
    {
        var results = new List<ControllerInfo>();
        var covered = new HashSet<RawGameController>();

        try
        {
            foreach (var gamepad in Gamepad.Gamepads)
            {
                var raw = RawGameController.FromGameController(gamepad);
                if (raw is not null)
                    covered.Add(raw);

                results.Add(new ControllerInfo(
                    raw?.DisplayName is { Length: > 0 } name ? name : "Manette Xbox / compatible",
                    "XInput",
                    TryGetBatteryPercent(gamepad)));
            }

            foreach (var raw in RawGameController.RawGameControllers)
            {
                if (covered.Contains(raw))
                    continue;

                results.Add(new ControllerInfo(
                    raw.DisplayName is { Length: > 0 } name ? name : "Manette générique",
                    "HID générique",
                    TryGetBatteryPercent(raw)));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Détection des manettes impossible", ex);
        }

        return results;
    }

    /// <summary>Seuil de déviation au repos au-delà duquel un stick est suspecté de drift.</summary>
    internal const double DriftThreshold = 0.08;

    /// <summary>Lecture instantanée des sticks de la première manette XInput, ou null si aucune.</summary>
    public static StickReading? ReadPrimaryStickPositions()
    {
        try
        {
            var gamepad = Gamepad.Gamepads.FirstOrDefault();
            if (gamepad is null)
                return null;

            var reading = gamepad.GetCurrentReading();
            return new StickReading(
                reading.LeftThumbstickX, reading.LeftThumbstickY,
                reading.RightThumbstickX, reading.RightThumbstickY);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture des sticks de manette impossible", ex);
            return null;
        }
    }

    /// <summary>
    /// Vrai si un stick dévie au-delà du seuil alors qu'il devrait être au
    /// centre — calcul pur pour être testable. L'appelant ne doit invoquer
    /// ceci que quand l'utilisateur ne touche pas la manette.
    /// </summary>
    internal static bool HasDrift(StickReading reading, double threshold = DriftThreshold)
    {
        return Math.Abs(reading.LeftX) > threshold || Math.Abs(reading.LeftY) > threshold
            || Math.Abs(reading.RightX) > threshold || Math.Abs(reading.RightY) > threshold;
    }

    private static int? TryGetBatteryPercent(IGameControllerBatteryInfo controller)
    {
        try
        {
            var report = controller.TryGetBatteryReport();
            if (report is null)
                return null;

            var full = report.FullChargeCapacityInMilliwattHours;
            var remaining = report.RemainingCapacityInMilliwattHours;
            if (full is > 0 && remaining is not null)
                return (int)Math.Round(remaining.Value * 100.0 / full.Value);

            return null;
        }
        catch
        {
            return null;
        }
    }
}
