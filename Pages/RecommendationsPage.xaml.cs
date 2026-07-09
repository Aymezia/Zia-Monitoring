using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZiaMonitoring_App.Application;

namespace ZiaMonitoring_App.Pages;

public sealed partial class RecommendationsPage : Page
{
    private readonly App _app;
    private PcAuditReport? _latestReport;

    public RecommendationsPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        AuditStatusLabel.Text = "Cliquez sur 'Lancer l'audit complet' pour analyser sécurité, confidentialité, stabilité et matériel en une passe.";
    }

    private async void RunAudit_Click(object sender, RoutedEventArgs e)
    {
        RunAuditButton.IsEnabled = false;
        AuditStatusLabel.Text = "Analyse en cours (sécurité, débloat, démarrage, stabilité, matériel)…";
        try
        {
            var report = await Task.Run(() =>
            {
                var frame = _app.MonitoringService.CaptureFrame();
                return _app.PcAudit.RunFullAudit(frame.Snapshot, frame.Profile, _app.MonitoringService.MemoryLeakSuspects);
            });

            _latestReport = report;
            ScoreValueLabel.Text = $"{report.Score}";
            var appResources = Microsoft.UI.Xaml.Application.Current.Resources;
            ScoreValueLabel.Foreground = report.Score switch
            {
                >= 75 => (Microsoft.UI.Xaml.Media.Brush)appResources["ZiaGreenBrush"],
                >= 50 => (Microsoft.UI.Xaml.Media.Brush)appResources["ZiaAmberBrush"],
                _ => (Microsoft.UI.Xaml.Media.Brush)appResources["ZiaRedBrush"]
            };
            ScoreQualifierLabel.Text = report.ScoreLabel[(report.ScoreLabel.IndexOf('—') + 2)..];
            SummaryLabel.Text = report.SummaryLabel;
            AuditStatusLabel.Text = $"Audit terminé à {report.GeneratedAt:HH:mm:ss}.";
            _app.Achievements.Increment("pc_audits_run");

            ApplyCategoryFilter();
        }
        catch (Exception ex)
        {
            AuditStatusLabel.Text = "Audit impossible.";
            Infrastructure.AppLog.Warn("Audit PC complet en échec", ex);
        }
        finally
        {
            RunAuditButton.IsEnabled = true;
        }
    }

    private void CategoryFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyCategoryFilter();

    private void ApplyCategoryFilter()
    {
        if (_latestReport is null)
            return;

        var selectedIndex = CategoryFilterCombo.SelectedIndex;
        FindingsList.ItemsSource = selectedIndex switch
        {
            1 => Filter(AuditCategory.Securite),
            2 => Filter(AuditCategory.Confidentialite),
            3 => Filter(AuditCategory.Stabilite),
            4 => Filter(AuditCategory.Materiel),
            5 => Filter(AuditCategory.Performance),
            6 => Filter(AuditCategory.Reseau),
            _ => _latestReport.Findings
        };
    }

    private List<PcAuditFinding> Filter(AuditCategory category) =>
        _latestReport!.Findings.Where(f => f.Category == category).ToList();
}
