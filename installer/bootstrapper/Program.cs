using System.Diagnostics;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var exePath = Application.ExecutablePath;
        var exeDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
        var msiPath = Path.Combine(exeDir, "ZiaMonitoring-Setup.msi");

        if (!File.Exists(msiPath))
        {
            MessageBox.Show(
                "Impossible de trouver ZiaMonitoring-Setup.msi dans le meme dossier que SetupBootstrap.exe.",
                "Zia Monitoring Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 2;
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{msiPath}\"",
                UseShellExecute = true
            }
        };

        try
        {
            process.Start();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Echec du lancement de l'installation: {ex.Message}",
                "Zia Monitoring Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}
