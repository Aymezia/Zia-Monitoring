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
        RefreshPrivacy();
        RefreshDebloat();
        RefreshDeviceAudit();
    }

    private void RefreshDeviceAudit_Click(object sender, RoutedEventArgs e) => RefreshDeviceAudit();

    private void RefreshDeviceAudit()
    {
        try
        {
            var entries = _app.DeviceAccessAudit.ScanWebcamAccess()
                .Concat(_app.DeviceAccessAudit.ScanMicrophoneAccess())
                .OrderByDescending(entry => entry.IsCurrentlyActive)
                .ThenByDescending(entry => entry.LastUsedStop ?? entry.LastUsedStart ?? DateTime.MinValue)
                .Take(20)
                .ToList();

            DeviceAuditList.ItemsSource = entries;
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Audit webcam/micro impossible", ex);
        }
    }

    private void RefreshDebloat()
    {
        try
        {
            var items = _app.Debloat.Scan();
            DebloatList.ItemsSource = items;
            var toClean = items.Count(i => !i.IsClean);
            DebloatStatusLabel.Text = toClean == 0
                ? "Tout est déjà nettoyé."
                : $"{toClean} élément(s) encore actif(s) sur {items.Count}.";
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Scan de débloat impossible", ex);
        }
    }

    private void RefreshDebloat_Click(object sender, RoutedEventArgs e) => RefreshDebloat();

    private void CleanDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ZiaMonitoring_App.Application.DebloatItem item })
        {
            var (success, message) = _app.Debloat.Clean(item.Category, item.Key);
            DebloatStatusLabel.Text = message;
            if (success)
                RefreshDebloat();
        }
    }

    private void UndoDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ZiaMonitoring_App.Application.DebloatItem item })
        {
            var (success, message) = _app.Debloat.Undo(item.Category, item.Key);
            DebloatStatusLabel.Text = message;
            if (success)
                RefreshDebloat();
        }
    }

    private async void CleanAllDebloat_Click(object sender, RoutedEventArgs e)
    {
        CleanAllDebloatButton.IsEnabled = false;
        try
        {
            if (_app.SettingsService.Load().EnableRestorePointBeforeRiskyActions)
            {
                DebloatStatusLabel.Text = "Création d'un point de restauration…";
                await Task.Run(() => _app.RestorePoint.CreateRestorePoint("Zia Monitoring - avant Débloat"));
            }

            var items = _app.Debloat.Scan();
            var count = await Task.Run(() => _app.Debloat.CleanAll(items));
            DebloatStatusLabel.Text = $"{count} élément(s) nettoyé(s).";
            RefreshDebloat();
        }
        finally
        {
            CleanAllDebloatButton.IsEnabled = true;
        }
    }

    private void RefreshPrivacy()
    {
        try
        {
            PrivacyList.ItemsSource = _app.PrivacyScanner.Scan();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Scan de confidentialité impossible", ex);
        }
    }

    private void FixPrivacy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            _app.PrivacyScanner.Fix(key);
            RefreshPrivacy();
        }
    }

    private void FixAllPrivacy_Click(object sender, RoutedEventArgs e)
    {
        var fixedCount = _app.PrivacyScanner.FixAll();
        RefreshPrivacy();
        ScanStatusLabel.Text = $"{fixedCount} réglage(s) de confidentialité corrigé(s).";
    }

    private async void CheckHibp_Click(object sender, RoutedEventArgs e)
    {
        var password = HibpPasswordBox.Password;
        if (string.IsNullOrEmpty(password))
        {
            HibpResultLabel.Text = "Saisissez un mot de passe à vérifier.";
            return;
        }

        HibpCheckButton.IsEnabled = false;
        HibpResultLabel.Text = "Vérification en cours…";
        try
        {
            var count = await _app.Hibp.CheckPasswordAsync(password);
            HibpPasswordBox.Password = string.Empty;

            HibpResultLabel.Text = count switch
            {
                null => "Service injoignable (hors ligne ?). Réessayez plus tard.",
                0 => "✔ Introuvable dans les fuites connues. (Cela ne garantit pas qu'il soit robuste.)",
                _ => $"⚠ Compromis : ce mot de passe apparaît {count:N0} fois dans des fuites de données. Changez-le partout où il est utilisé."
            };
        }
        finally
        {
            HibpCheckButton.IsEnabled = true;
        }
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
