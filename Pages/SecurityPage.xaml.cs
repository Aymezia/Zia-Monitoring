using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Core.Models;

namespace ZiaMonitoring_App.Pages;

public sealed partial class SecurityPage : Page
{
    private readonly App _app;
    private SecurityReport? _latestReport;
    private IReadOnlyList<ZiaMonitoring_App.Application.DebloatItem> _allDebloatItems = [];
    // Le SelectedIndex="0" du XAML déclenche SelectionChanged pendant InitializeComponent,
    // avant que DebloatList/DebloatStatusLabel ne soient connectés : ce garde-fou évite le
    // NullReferenceException qui bloquait l'ouverture de la page.
    private bool _debloatFilterLoading = true;

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

    private async void RefreshExtensions_Click(object sender, RoutedEventArgs e)
    {
        RefreshExtensionsButton.IsEnabled = false;
        ExtensionsStatusLabel.Text = "Analyse en cours…";
        try
        {
            var extensions = await Task.Run(() => _app.BrowserExtensions.ScanAll());
            ExtensionsList.ItemsSource = extensions;
            ExtensionsStatusLabel.Text = extensions.Count == 0
                ? "Aucune extension détectée (ou navigateurs non installés)."
                : $"{extensions.Count} extension(s) détectée(s).";
        }
        finally
        {
            RefreshExtensionsButton.IsEnabled = true;
        }
    }

    private void RefreshDeviceAudit()
    {
        try
        {
            var entries = _app.DeviceAccessAudit.ScanWebcamAccess()
                .Concat(_app.DeviceAccessAudit.ScanMicrophoneAccess())
                .Concat(_app.DeviceAccessAudit.ScanDocumentsAccess())
                .Concat(_app.DeviceAccessAudit.ScanPicturesAccess())
                .Concat(_app.DeviceAccessAudit.ScanBroadFileSystemAccess())
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
            _allDebloatItems = _app.Debloat.Scan();
            ApplyDebloatFilter();
        }
        catch (Exception ex)
        {
            Infrastructure.AppLog.Warn("Scan de débloat impossible", ex);
        }
    }

    private void ApplyDebloatFilter()
    {
        var selectedIndex = DebloatCategoryFilterCombo.SelectedIndex;
        var items = selectedIndex switch
        {
            1 => _allDebloatItems.Where(i => i.Category == ZiaMonitoring_App.Application.DebloatCategory.Telemetry).ToList(),
            2 => _allDebloatItems.Where(i => i.Category == ZiaMonitoring_App.Application.DebloatCategory.ScheduledTask).ToList(),
            3 => _allDebloatItems.Where(i => i.Category == ZiaMonitoring_App.Application.DebloatCategory.BloatwareApp).ToList(),
            _ => _allDebloatItems
        };

        DebloatList.ItemsSource = items;
        var toClean = items.Count(i => !i.IsClean);
        DebloatStatusLabel.Text = toClean == 0
            ? "Tout est déjà nettoyé (dans cette catégorie)."
            : $"{toClean} élément(s) encore actif(s) sur {items.Count} (dans cette catégorie).";
    }

    private void DebloatCategoryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_debloatFilterLoading)
        {
            _debloatFilterLoading = false;
            return;
        }

        ApplyDebloatFilter();
    }

    private void RefreshDebloat_Click(object sender, RoutedEventArgs e) => RefreshDebloat();

    private static bool RequiresElevation(ZiaMonitoring_App.Application.DebloatCategory category) =>
        category is ZiaMonitoring_App.Application.DebloatCategory.Telemetry or ZiaMonitoring_App.Application.DebloatCategory.ScheduledTask;

    private async void CleanDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ZiaMonitoring_App.Application.DebloatItem item })
            return;

        if (RequiresElevation(item.Category) && !await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "Le nettoyage de cet élément"))
            return;

        var (success, message) = _app.Debloat.Clean(item.Category, item.Key);
        DebloatStatusLabel.Text = message;
        if (success)
            RefreshDebloat();
    }

    private async void UndoDebloat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ZiaMonitoring_App.Application.DebloatItem item })
            return;

        if (RequiresElevation(item.Category) && !await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "La restauration de cet élément"))
            return;

        var (success, message) = _app.Debloat.Undo(item.Category, item.Key);
        DebloatStatusLabel.Text = message;
        if (success)
            RefreshDebloat();
    }

    private async void CleanAllDebloat_Click(object sender, RoutedEventArgs e)
    {
        var settings = _app.SettingsService.Load();
        var needsElevation = _allDebloatItems.Any(i => !i.IsClean && RequiresElevation(i.Category)) || settings.EnableRestorePointBeforeRiskyActions;
        if (needsElevation && !await AdminElevationPrompt.EnsureElevatedAsync(XamlRoot, "Le nettoyage complet du débloat"))
            return;

        CleanAllDebloatButton.IsEnabled = false;
        try
        {
            if (settings.EnableRestorePointBeforeRiskyActions)
            {
                DebloatStatusLabel.Text = "Création d'un point de restauration…";
                await Task.Run(() => _app.RestorePoint.CreateRestorePoint("Zia Monitoring - avant Débloat"));
            }

            var items = _app.Debloat.Scan();
            var (count, failures) = await Task.Run(() => _app.Debloat.CleanAll(items));
            _app.Achievements.Increment("debloat_cleaned", count);
            DebloatStatusLabel.Text = failures.Count == 0
                ? $"{count} élément(s) nettoyé(s)."
                : $"{count} élément(s) nettoyé(s). Échec ({failures.Count}) : {string.Join(" · ", failures.Select(f => $"{f.Name} — {f.Reason}"))}";
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
            _app.Achievements.Increment("security_scans");
            RiskScoreLabel.Text = $"Score de risque global: {report.RiskScore}/100";

            FirewallLabel.Text = report.FirewallEnabled
                ? "Pare-feu Windows: ACTIF"
                : "Pare-feu Windows: DESACTIVE (risque !)";
            FirewallLabel.Foreground = report.FirewallEnabled
                ? Microsoft.UI.Xaml.Application.Current.Resources["ZiaVioletBrush"] as Microsoft.UI.Xaml.Media.Brush
                : Microsoft.UI.Xaml.Application.Current.Resources["ZiaRedBrush"] as Microsoft.UI.Xaml.Media.Brush;

            UacLabel.Text = report.UacEnabled
                ? "UAC (controle de compte): ACTIF"
                : "UAC: DESACTIVE (risque eleve !)";
            UacLabel.Foreground = report.UacEnabled
                ? Microsoft.UI.Xaml.Application.Current.Resources["ZiaVioletBrush"] as Microsoft.UI.Xaml.Media.Brush
                : Microsoft.UI.Xaml.Application.Current.Resources["ZiaRedBrush"] as Microsoft.UI.Xaml.Media.Brush;

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

            AntivirusList.ItemsSource = report.AntivirusProducts;
            AntivirusStatusLabel.Text = report.AntivirusProducts.Count == 0
                ? "Aucun antivirus enregistré auprès du Centre de sécurité Windows."
                : report.HasAntivirusConflict
                    ? $"⚠ {report.AntivirusProducts.Count(p => p.IsEnabled)} antivirus actifs en même temps : conflit probable, désactivez-en un."
                    : $"{report.AntivirusProducts.Count} antivirus détecté(s), aucun conflit.";

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
