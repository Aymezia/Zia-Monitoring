using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ZiaMonitoring_App.Infrastructure;

/// <summary>
/// L'application démarre sans élévation (app.manifest: asInvoker). Les
/// actions qui touchent réellement le système (registre HKLM, netsh, DISM/SFC,
/// pilote de capteurs matériel) passent par <see cref="RelaunchElevated"/>
/// pour proposer une relance en administrateur au moment précis où c'est
/// nécessaire, plutôt que d'imposer l'UAC à chaque lancement.
/// </summary>
public static class AdminElevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Relance l'exécutable courant avec élévation (verbe "runas") puis
    /// termine le processus actuel. Retourne false si l'utilisateur a
    /// annulé l'invite UAC ou si la relance a échoué — dans ce cas le
    /// processus courant continue de tourner normalement.
    /// </summary>
    /// <param name="resumeArgs">
    /// Arguments de ligne de commande transmis à l'instance élevée (voir
    /// App.PendingDebloatResume) pour qu'elle termine automatiquement
    /// l'action interrompue par le redémarrage, plutôt que d'obliger
    /// l'utilisateur à recliquer une fois relancé.
    /// </param>
    public static bool RelaunchElevated(IEnumerable<string>? resumeArgs = null)
    {
        try
        {
            var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                return false;

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            if (resumeArgs is not null)
                foreach (var arg in resumeArgs)
                    psi.ArgumentList.Add(arg);

            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex)
        {
            // ERROR_CANCELLED (1223) : l'utilisateur a refusé l'invite UAC.
            AppLog.Info($"Relance en administrateur annulée ({ex.NativeErrorCode})");
            return false;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Relance en administrateur impossible", ex);
            return false;
        }
    }
}
