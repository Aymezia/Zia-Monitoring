using System.Diagnostics;
using System.IO.Compression;

namespace ZiaMonitoring_App.Application;

/// <summary>
/// Applique une mise à jour détectée par UpdateCheckService, sans action
/// manuelle de l'utilisateur au-delà d'un clic :
/// - Build installé (Inno Setup, détecté via unins000.exe à côté de l'exe) :
///   télécharge le nouveau Setup.exe et le lance en silencieux
///   (/VERYSILENT), qui relance l'app tout seul une fois terminé
///   (voir [Run] dans ZiaMonitoringSetup.iss, sans le flag skipifsilent).
/// - Build portable : télécharge le nouveau zip, écrit un petit script
///   PowerShell qui attend la fermeture du process courant, remplace les
///   fichiers en place puis relance l'exe — l'app portable reste portable
///   (aucune écriture registre), juste ses fichiers sont remplacés.
/// Dans les deux cas, l'app courante se ferme juste après avoir lancé le
/// programme de mise à jour, pour libérer ses fichiers.
/// </summary>
public sealed class SelfUpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    /// <summary>Vrai si l'exécutable courant tourne depuis une installation Inno Setup (présence de unins000.exe).</summary>
    public static bool IsInstalledBuild(string? exeDirectory = null)
    {
        var dir = exeDirectory ?? AppContext.BaseDirectory;
        return File.Exists(Path.Combine(dir, "unins000.exe"));
    }

    public async Task<(bool Success, string Message)> UpdateAsync(UpdateInfo info, Action<string>? onProgress = null, CancellationToken ct = default)
    {
        var exeDirectory = AppContext.BaseDirectory;
        var installed = IsInstalledBuild(exeDirectory);

        try
        {
            if (installed)
            {
                if (string.IsNullOrWhiteSpace(info.SetupExeUrl))
                    return (false, "Aucun installeur disponible pour cette version (asset Setup.exe manquant sur la release).");

                return await UpdateInstalledAsync(info.SetupExeUrl, onProgress, ct).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(info.PortableZipUrl))
                return (false, "Aucune archive portable disponible pour cette version (asset .zip manquant sur la release).");

            return await UpdatePortableAsync(info.PortableZipUrl, exeDirectory, onProgress, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Error("Mise à jour automatique impossible", ex);
            return (false, $"Mise à jour impossible : {ex.Message}");
        }
    }

    private static async Task<(bool, string)> UpdateInstalledAsync(string setupUrl, Action<string>? onProgress, CancellationToken ct)
    {
        onProgress?.Invoke("Téléchargement de l'installeur…");
        var tempExe = Path.Combine(Path.GetTempPath(), $"ZiaMonitoring-Setup-{Guid.NewGuid():N}.exe");
        await DownloadFileAsync(setupUrl, tempExe, ct).ConfigureAwait(false);

        onProgress?.Invoke("Lancement de la mise à jour silencieuse…");
        Process.Start(new ProcessStartInfo(tempExe, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NOCANCEL")
        {
            UseShellExecute = true
        });

        return (true, "Mise à jour lancée : l'application va se fermer puis redémarrer automatiquement.");
    }

    private static async Task<(bool, string)> UpdatePortableAsync(string zipUrl, string exeDirectory, Action<string>? onProgress, CancellationToken ct)
    {
        onProgress?.Invoke("Téléchargement de l'archive portable…");
        var workDir = Path.Combine(Path.GetTempPath(), $"ZiaMonitoringUpdate-{Guid.NewGuid():N}");
        var extractDir = Path.Combine(workDir, "new");
        Directory.CreateDirectory(extractDir);
        var tempZip = Path.Combine(workDir, "update.zip");

        await DownloadFileAsync(zipUrl, tempZip, ct).ConfigureAwait(false);

        onProgress?.Invoke("Extraction de la mise à jour…");
        ZipFile.ExtractToDirectory(tempZip, extractDir, overwriteFiles: true);

        var exeName = Path.GetFileName(Environment.ProcessPath) ?? "ZiaMonitoring.App.exe";
        var currentPid = Environment.ProcessId;
        var scriptPath = Path.Combine(workDir, "apply-update.ps1");
        File.WriteAllText(scriptPath, BuildPortableUpdateScript(currentPid, extractDir, exeDirectory, exeName, workDir));

        onProgress?.Invoke("Application de la mise à jour…");
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return (true, "Mise à jour lancée : l'application va se fermer puis redémarrer automatiquement.");
    }

    /// <summary>
    /// Script généré : attend la fin du process courant (par PID), copie les
    /// nouveaux fichiers par-dessus l'installation existante, relance l'exe,
    /// puis nettoie son propre dossier de travail temporaire.
    /// </summary>
    internal static string BuildPortableUpdateScript(int waitForPid, string sourceDir, string destDir, string exeName, string workDir) => $$"""
        $ErrorActionPreference = 'SilentlyContinue'
        try { Wait-Process -Id {{waitForPid}} -Timeout 30 } catch {}
        Start-Sleep -Milliseconds 500
        Copy-Item -Path "{{sourceDir}}\*" -Destination "{{destDir}}" -Recurse -Force
        Start-Process -FilePath "{{Path.Combine(destDir, exeName)}}"
        Start-Sleep -Milliseconds 500
        Remove-Item -Path "{{workDir}}" -Recurse -Force
        """;

    private static async Task DownloadFileAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var fileStream = File.Create(destinationPath);
        await response.Content.CopyToAsync(fileStream, ct).ConfigureAwait(false);
    }
}
