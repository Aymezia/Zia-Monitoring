using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace ZiaMonitoring_App.Application;

public sealed record BatteryHealthInfo(string Name, double DesignCapacityMWh, double FullChargeCapacityMWh, int? CycleCount)
{
    public double WearPercent => DesignCapacityMWh > 0
        ? Math.Clamp((1 - FullChargeCapacityMWh / DesignCapacityMWh) * 100, 0, 100)
        : 0;

    public string Label =>
        $"{Name} : {FullChargeCapacityMWh / 1000:F1} Wh utiles sur {DesignCapacityMWh / 1000:F1} Wh d'origine "
        + $"({WearPercent:F0} % d'usure{(CycleCount is { } c ? $", {c} cycle(s)" : "")}).";
}

/// <summary>
/// Santé batterie via le rapport que Windows génère lui-même
/// (powercfg /batteryreport) + bascule automatique du plan d'alimentation
/// selon la source (secteur → hautes performances, batterie → utilisation
/// normale). Sans effet sur un PC fixe : la bascule ne s'active que si une
/// batterie est présente.
/// </summary>
public sealed class BatteryService
{
    private const string HighPerformanceScheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private bool? _lastOnAc;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    /// <summary>null = impossible à déterminer ; BatteryFlag 128 = pas de batterie.</summary>
    public static (bool OnAc, bool HasBattery)? GetPowerSource()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
                return null;

            return (status.ACLineStatus == 1, (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255);
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<BatteryHealthInfo> GetBatteryHealth()
    {
        try
        {
            var reportPath = Path.Combine(Path.GetTempPath(), $"zia-battery-{Guid.NewGuid():N}.xml");
            try
            {
                var psi = new ProcessStartInfo("powercfg", $"/batteryreport /xml /output \"{reportPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(15000);
                }

                if (!File.Exists(reportPath))
                    return [];

                return ParseBatteryReport(File.ReadAllText(reportPath));
            }
            finally
            {
                try { File.Delete(reportPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Génération du rapport de batterie impossible", ex);
            return [];
        }
    }

    /// <summary>Parse le XML de powercfg /batteryreport, indépendamment du namespace exact.</summary>
    internal static IReadOnlyList<BatteryHealthInfo> ParseBatteryReport(string xml)
    {
        var result = new List<BatteryHealthInfo>();

        try
        {
            var doc = XDocument.Parse(xml);
            foreach (var battery in doc.Descendants().Where(e => e.Name.LocalName == "Battery"))
            {
                double ReadValue(string localName)
                {
                    var element = battery.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
                    return element is not null && double.TryParse(element.Value, out var v) ? v : 0;
                }

                var design = ReadValue("DesignCapacity");
                var full = ReadValue("FullChargeCapacity");
                if (design <= 0 && full <= 0)
                    continue;

                var cycleElement = battery.Descendants().FirstOrDefault(e => e.Name.LocalName == "CycleCount");
                int? cycles = cycleElement is not null && int.TryParse(cycleElement.Value, out var c) && c > 0 ? c : null;

                var name = battery.Descendants().FirstOrDefault(e => e.Name.LocalName == "Id")?.Value.Trim();
                result.Add(new BatteryHealthInfo(string.IsNullOrWhiteSpace(name) ? "Batterie" : name, design, full, cycles));
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture du rapport de batterie impossible", ex);
        }

        return result;
    }

    /// <summary>
    /// Applique le plan d'alimentation correspondant à la source si elle a
    /// changé depuis le dernier appel. Retourne le message d'action, ou null
    /// si rien à faire (pas de batterie, pas de changement, désactivé).
    /// </summary>
    public string? ApplyPowerSourceProfile()
    {
        var source = GetPowerSource();
        if (source is not { HasBattery: true } s)
            return null;

        if (_lastOnAc == s.OnAc)
            return null;

        var firstRun = _lastOnAc is null;
        _lastOnAc = s.OnAc;
        if (firstRun)
            return null; // état initial : on n'écrase pas le choix courant de l'utilisateur.

        var scheme = GetTargetScheme(s.OnAc);
        try
        {
            var psi = new ProcessStartInfo("powercfg", $"/setactive {scheme}")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);

            return s.OnAc
                ? "Secteur branché : plan Hautes performances activé."
                : "Sur batterie : plan Utilisation normale activé.";
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Bascule du plan d'alimentation impossible", ex);
            return null;
        }
    }

    internal static string GetTargetScheme(bool onAc) => onAc ? HighPerformanceScheme : BalancedScheme;

    /// <summary>À appeler quand l'option est désactivée, pour rebasculer proprement à la prochaine activation.</summary>
    public void ResetPowerSourceTracking() => _lastOnAc = null;
}
