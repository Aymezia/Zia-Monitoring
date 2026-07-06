using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Pages;

public sealed partial class SecurityPage : Page
{
    private readonly App _app;
    private SecurityReport? _latestReport;

    public SecurityPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        ScanStatusLabel.Text = "Cliquez sur 'Lancer l'analyse' pour demarrer.";
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        ScanButton.IsEnabled = false;
        ExportSecurityButton.IsEnabled = false;
        ScanStatusLabel.Text = "Analyse en cours...";

        try
        {
            var report = await Task.Run(() => _app.SecurityScanner.BuildReport());
            _latestReport = report;
            ExportSecurityButton.IsEnabled = true;
            RiskScoreLabel.Text = $"Score de risque global: {report.RiskScore}/100";

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

            OpenPortsList.ItemsSource = report.OpenPorts;
            OpenPortsStatusLabel.Text = report.OpenPorts.Count == 0
                ? "Aucun port TCP ecoute detecte."
                : $"{report.OpenPorts.Count} port(s) TCP ecoute liste(s).";

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

            SignatureList.ItemsSource = report.MaliciousProcessMatches;
            SignatureStatusLabel.Text = report.MaliciousProcessMatches.Count == 0
                ? "Aucune signature SHA256 connue detectee dans les processus accessibles."
                : $"{report.MaliciousProcessMatches.Count} processus avec signature connue detecte(s) !";

            HookList.ItemsSource = report.KeyloggerHookWarnings;
            HookStatusLabel.Text = report.KeyloggerHookWarnings.Count == 0
                ? "Aucun indicateur de hook clavier global detecte."
                : $"{report.KeyloggerHookWarnings.Count} indicateur(s) de hook clavier detecte(s) !";

            ScanStatusLabel.Text = $"Analyse terminee - {DateTime.Now:HH:mm:ss}";
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

    private async void ExportSecurity_Click(object sender, RoutedEventArgs e)
    {
        if (_latestReport is null)
            return;

        try
        {
            var (htmlPath, pdfPath) = _app.SecurityReportExporter.Export(_latestReport);
            ScanStatusLabel.Text = $"Rapports enregistres: {htmlPath} | {pdfPath}";
            var psi = new System.Diagnostics.ProcessStartInfo(htmlPath) { UseShellExecute = true };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Export securite", ex.Message);
        }
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = "OK",
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }
}
