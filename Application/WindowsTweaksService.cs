using System.Diagnostics;
using Microsoft.Win32;

namespace ZiaMonitoring_App.Application;

public enum TweakState { Optimized, Default, Unknown }

public sealed record WindowsTweak(string Id, string Name, string Description, TweakState State, string StateLabel)
{
    public bool IsOptimized => State == TweakState.Optimized;
}

/// <summary>
/// Réglages Windows connus pour le jeu/les performances, chacun réversible :
/// pour chaque tweak on lit l'état courant, on peut appliquer la valeur
/// "optimisée" et revenir au défaut Windows. Toutes les écritures HKLM /
/// services / powercfg nécessitent les droits administrateur — l'appelant
/// (page Boost) demande l'élévation avant Apply/Revert. Les fonctions
/// d'interprétation (valeur brute → état) sont pures et testables.
/// </summary>
public sealed class WindowsTweaksService
{
    private const string GraphicsDriversKey = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string MultimediaProfileKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string VisualEffectsKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string HvciKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
    private const string TcpInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    public IReadOnlyList<WindowsTweak> Scan()
    {
        return
        [
            new WindowsTweak("hags", "Planification GPU accélérée (HAGS)",
                "Laisse le GPU gérer sa propre file de rendu. Bénéfice variable selon la carte et les jeux — à tester chez soi.",
                HagsState(ReadHklmDword(GraphicsDriversKey, "HwSchMode")), HagsLabel(ReadHklmDword(GraphicsDriversKey, "HwSchMode"))),

            new WindowsTweak("network-throttling", "Bridage réseau multimédia",
                "Windows limite le débit réseau pour réserver du temps au multimédia. Le désactiver réduit la latence en jeu en ligne.",
                NetworkThrottlingState(ReadHklmDword(MultimediaProfileKey, "NetworkThrottlingIndex")),
                NetworkThrottlingLabel(ReadHklmDword(MultimediaProfileKey, "NetworkThrottlingIndex"))),

            new WindowsTweak("system-responsiveness", "Réserve CPU multimédia",
                "Windows réserve 20 % du CPU aux tâches multimédia par défaut. La passer à 0 rend ce temps aux jeux.",
                SystemResponsivenessState(ReadHklmDword(MultimediaProfileKey, "SystemResponsiveness")),
                SystemResponsivenessLabel(ReadHklmDword(MultimediaProfileKey, "SystemResponsiveness"))),

            new WindowsTweak("nagle", "Algorithme de Nagle (TCP)",
                "Regroupe les petits paquets TCP, ce qui ajoute de la latence. Le désactiver aide les jeux en ligne (au prix d'un léger surcoût de bande passante).",
                NagleState(AllInterfacesHaveNagleDisabled()), AllInterfacesHaveNagleDisabled() ? "Désactivé (optimisé)" : "Actif (défaut Windows)"),

            new WindowsTweak("ultimate-power", "Plan d'alimentation « Performances ultimes »",
                "Plan caché par Windows qui supprime les micro-mises en veille des composants. Idéal sur PC fixe branché au secteur.",
                IsUltimatePerformanceActive() ? TweakState.Optimized : TweakState.Default,
                IsUltimatePerformanceActive() ? "Actif" : "Inactif"),

            new WindowsTweak("sysmain", "SysMain (Superfetch)",
                "Précharge les applications en RAM. Inutile sur SSD (les temps d'accès sont déjà négligeables) et génère de l'activité disque continue.",
                SysMainState(ReadServiceStartType("SysMain")), SysMainLabel(ReadServiceStartType("SysMain"))),

            new WindowsTweak("fast-startup", "Démarrage rapide",
                "Hybride veille/arrêt qui cause des bugs classiques (périphériques, dual-boot, mises à jour non appliquées). Le désactiver donne un arrêt propre.",
                FastStartupState(ReadHklmDword(PowerKey, "HiberbootEnabled")), FastStartupLabel(ReadHklmDword(PowerKey, "HiberbootEnabled"))),

            new WindowsTweak("visual-effects", "Effets visuels Windows",
                "Animations et transparence. Le profil « performances » les coupe — utile sur PC modeste.",
                VisualEffectsState(ReadHkcuDword(VisualEffectsKey, "VisualFXSetting")), VisualEffectsLabel(ReadHkcuDword(VisualEffectsKey, "VisualFXSetting"))),

            new WindowsTweak("hvci", "Intégrité de la mémoire (Core Isolation / HVCI)",
                "Sécurité par virtualisation qui coûte souvent 5 à 10 % de FPS. La désactiver améliore les performances en jeu, mais réduit la protection contre certaines attaques bas niveau — à ne faire qu'en connaissance de cause.",
                HvciState(ReadHklmDword(HvciKey, "Enabled")), HvciLabel(ReadHklmDword(HvciKey, "Enabled"))),
        ];
    }

    public (bool Success, string Message) Apply(string id) => SetTweak(id, optimize: true);
    public (bool Success, string Message) Revert(string id) => SetTweak(id, optimize: false);

    private (bool, string) SetTweak(string id, bool optimize)
    {
        try
        {
            switch (id)
            {
                case "hags":
                    WriteHklmDword(GraphicsDriversKey, "HwSchMode", optimize ? 2 : 1);
                    return (true, optimize ? "HAGS activé — redémarrage requis." : "HAGS désactivé — redémarrage requis.");

                case "network-throttling":
                    WriteHklmDword(MultimediaProfileKey, "NetworkThrottlingIndex", optimize ? unchecked((int)0xFFFFFFFF) : 10);
                    return (true, optimize ? "Bridage réseau désactivé." : "Bridage réseau restauré (défaut Windows).");

                case "system-responsiveness":
                    WriteHklmDword(MultimediaProfileKey, "SystemResponsiveness", optimize ? 0 : 20);
                    return (true, optimize ? "Réserve CPU multimédia mise à 0." : "Réserve CPU multimédia restaurée (20 %).");

                case "nagle":
                    return SetNagle(optimize);

                case "ultimate-power":
                    return optimize ? EnableUltimatePerformance() : DisableUltimatePerformance();

                case "sysmain":
                    return SetSysMain(optimize);

                case "fast-startup":
                    WriteHklmDword(PowerKey, "HiberbootEnabled", optimize ? 0 : 1);
                    return (true, optimize ? "Démarrage rapide désactivé." : "Démarrage rapide réactivé.");

                case "visual-effects":
                    WriteHkcuDword(VisualEffectsKey, "VisualFXSetting", optimize ? 2 : 0);
                    return (true, optimize
                        ? "Profil « performances » appliqué — redémarrez l'Explorateur pour voir l'effet."
                        : "Effets visuels laissés au choix de Windows.");

                case "hvci":
                    WriteHklmDword(HvciKey, "Enabled", optimize ? 0 : 1);
                    return (true, optimize
                        ? "Intégrité de la mémoire désactivée — redémarrage requis pour libérer les performances."
                        : "Intégrité de la mémoire réactivée — redémarrage requis.");

                default:
                    return (false, "Réglage inconnu.");
            }
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn($"Réglage Windows '{id}' impossible", ex);
            return (false, ex.Message);
        }
    }

    // ---- Interprétations pures (testables) ----

    internal static TweakState HagsState(int? value) => value switch { 2 => TweakState.Optimized, 1 => TweakState.Default, _ => TweakState.Unknown };
    internal static string HagsLabel(int? value) => value switch { 2 => "Activé", 1 => "Désactivé", _ => "Indéterminé" };

    internal static TweakState NetworkThrottlingState(int? value) => value switch { unchecked((int)0xFFFFFFFF) => TweakState.Optimized, null => TweakState.Unknown, _ => TweakState.Default };
    internal static string NetworkThrottlingLabel(int? value) => value == unchecked((int)0xFFFFFFFF) ? "Désactivé (optimisé)" : value is null ? "Indéterminé" : "Actif (défaut Windows)";

    internal static TweakState SystemResponsivenessState(int? value) => value switch { 0 or 10 => TweakState.Optimized, null => TweakState.Unknown, _ => TweakState.Default };
    internal static string SystemResponsivenessLabel(int? value) => value switch { 0 => "0 % réservé (optimisé)", 10 => "10 % réservé", null => "Indéterminé", _ => $"{value} % réservé" };

    internal static TweakState NagleState(bool disabled) => disabled ? TweakState.Optimized : TweakState.Default;

    internal static TweakState SysMainState(int? startType) => startType switch { 4 => TweakState.Optimized, null => TweakState.Unknown, _ => TweakState.Default };
    internal static string SysMainLabel(int? startType) => startType switch { 4 => "Désactivé (optimisé SSD)", 2 => "Automatique", 3 => "Manuel", null => "Indéterminé", _ => "Actif" };

    internal static TweakState FastStartupState(int? value) => value switch { 0 => TweakState.Optimized, 1 => TweakState.Default, _ => TweakState.Unknown };
    internal static string FastStartupLabel(int? value) => value switch { 0 => "Désactivé", 1 => "Activé", _ => "Indéterminé" };

    internal static TweakState VisualEffectsState(int? value) => value switch { 2 => TweakState.Optimized, null => TweakState.Unknown, _ => TweakState.Default };
    internal static string VisualEffectsLabel(int? value) => value switch { 2 => "Performances", 1 => "Apparence", 3 => "Personnalisé", null => "Automatique", _ => "Automatique" };

    // Ici « optimisé » = HVCI désactivé (meilleur FPS) ; le défaut Windows récent est activé.
    internal static TweakState HvciState(int? value) => value switch { 0 => TweakState.Optimized, 1 => TweakState.Default, _ => TweakState.Unknown };
    internal static string HvciLabel(int? value) => value switch { 1 => "Activée", 0 => "Désactivée", _ => "Indéterminée" };

    // ---- Accès système ----

    private (bool, string) SetNagle(bool disable)
    {
        var value = disable ? 1 : 0;
        var touched = 0;
        using var interfaces = Registry.LocalMachine.OpenSubKey(TcpInterfacesKey, writable: true);
        if (interfaces is null)
            return (false, "Interfaces réseau introuvables dans le registre.");

        foreach (var name in interfaces.GetSubKeyNames())
        {
            using var iface = interfaces.OpenSubKey(name, writable: true);
            if (iface?.GetValue("DhcpIPAddress") is null && iface?.GetValue("IPAddress") is null)
                continue; // interface sans IP : ignorée.

            iface.SetValue("TcpAckFrequency", value, RegistryValueKind.DWord);
            iface.SetValue("TCPNoDelay", value, RegistryValueKind.DWord);
            touched++;
        }

        return (true, disable
            ? $"Nagle désactivé sur {touched} interface(s) — redémarrage requis."
            : $"Nagle réactivé sur {touched} interface(s) — redémarrage requis.");
    }

    private bool AllInterfacesHaveNagleDisabled()
    {
        try
        {
            using var interfaces = Registry.LocalMachine.OpenSubKey(TcpInterfacesKey, writable: false);
            if (interfaces is null)
                return false;

            var relevant = 0;
            var disabled = 0;
            foreach (var name in interfaces.GetSubKeyNames())
            {
                using var iface = interfaces.OpenSubKey(name, writable: false);
                if (iface?.GetValue("DhcpIPAddress") is null && iface?.GetValue("IPAddress") is null)
                    continue;

                relevant++;
                if (iface.GetValue("TcpAckFrequency") is 1 && iface.GetValue("TCPNoDelay") is 1)
                    disabled++;
            }

            return relevant > 0 && disabled == relevant;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Lecture de l'état Nagle impossible", ex);
            return false;
        }
    }

    private static (bool, string) EnableUltimatePerformance()
    {
        RunProcess("powercfg.exe", $"-duplicatescheme {UltimatePerformanceGuid}");
        var (code, _, err) = RunProcess("powercfg.exe", $"-setactive {UltimatePerformanceGuid}");
        return code == 0
            ? (true, "Plan « Performances ultimes » activé.")
            : (false, $"Activation impossible : {err}");
    }

    private static (bool, string) DisableUltimatePerformance()
    {
        // Repli sur le plan équilibré standard.
        RunProcess("powercfg.exe", "-setactive 381b4222-f694-41f0-9685-ff5bb260df2e");
        return (true, "Plan d'alimentation repassé sur « Normal ».");
    }

    private static (bool, string) SetSysMain(bool disable)
    {
        var (code, _, err) = RunProcess("sc.exe", $"config SysMain start= {(disable ? "disabled" : "auto")}");
        if (code != 0)
            return (false, $"Modification du service impossible : {err}");

        RunProcess("sc.exe", disable ? "stop SysMain" : "start SysMain");
        return (true, disable ? "SysMain désactivé et arrêté." : "SysMain réactivé (automatique).");
    }

    private static bool IsUltimatePerformanceActive()
    {
        try
        {
            var (_, output, _) = RunProcess("powercfg.exe", "-getactivescheme");
            return output.Contains(UltimatePerformanceGuid, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static int? ReadServiceStartType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            return key?.GetValue("Start") as int?;
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadHklmDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as int?;
        }
        catch
        {
            return null;
        }
    }

    private static int? ReadHkcuDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            return key?.GetValue(name) as int?;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteHklmDword(string subKey, string name, int value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Clé '{subKey}' inaccessible (droits administrateur requis).");
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static void WriteHkcuDword(string subKey, string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunProcess(string fileName, string arguments, int timeoutMs = 10000)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de démarrer {fileName}.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(timeoutMs);
        return (process.ExitCode, output, error);
    }
}
