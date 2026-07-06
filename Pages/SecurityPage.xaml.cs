using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class SecurityPage : Page
{
    private readonly App _app;

    public SecurityPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        ScanStatusLabel.Text = "Cliquez sur 'Lancer l'analyse' pour demarrer.";
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ScanStatusLabel.Text = "Analyse en cours...";

        try
        {
            var report = await Task.Run(() => _app.SecurityScanner.BuildReport());

            FirewallLabel.Text = report.FirewallEnabled
                ? "Pare-feu Windows: ACTIF"
                : "Pare-feu Windows: DESACTIVE (risque !)";
            FirewallLabel.Foreground = report.FirewallEnabled
                ? Resources["ZiaVioletBrush"] as Microsoft.UI.Xaml.Media.Brush
                : Resources["ZiaRedBrush"] as Microsoft.UI.Xaml.Media.Brush;

            UacLabel.Text = report.UacEnabled
                ? "UAC (controle de compte): ACTIF"
                : "UAC: DESACTIVE (risque eleve !)";
            UacLabel.Foreground = report.UacEnabled
                ? Resources["ZiaVioletBrush"] as Microsoft.UI.Xaml.Media.Brush
                : Resources["ZiaRedBrush"] as Microsoft.UI.Xaml.Media.Brush;

            SmartList.ItemsSource = report.DiskSmartWarnings;
            SmartStatusLabel.Text = report.DiskSmartWarnings.Count == 0
                ? "Tous les disques sont en bonne sante."
                : $"{report.DiskSmartWarnings.Count} avertissement(s) S.M.A.R.T detecte(s) !";

            DriversList.ItemsSource = report.ObsoleteDrivers;
            DriversStatusLabel.Text = report.ObsoleteDrivers.Count == 0
                ? "Aucun pilote obsolete detecte."
                : $"{report.ObsoleteDrivers.Count} pilote(s) obsolete(s) detecte(s).";

            SuspiciousList.ItemsSource = report.SuspiciousStartupEntries;
            SuspiciousStatusLabel.Text = report.SuspiciousStartupEntries.Count == 0
                ? "Aucune entree suspecte au demarrage."
                : $"{report.SuspiciousStartupEntries.Count} entree(s) suspecte(s) detectee(s) !";

            ScanStatusLabel.Text = $"Analyse terminee — {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            ScanStatusLabel.Text = $"Erreur: {ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }
}
