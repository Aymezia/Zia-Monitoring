using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class DetailedLogsPage : Page
{
    private readonly App _app;

    public DetailedLogsPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
    }

    private void RefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        var product = GetSelectedProduct();
        var report = _app.GamerTroubleshootingService.AutoCollectLogs(product);

        MetaText.Text = $"{report.CollectedAt:HH:mm:ss} | Lines scanned: {report.ObsLinesScanned} | OBS findings: {report.ObsLogFindings.Count} | Events: {report.WindowsEvents.Count}";
        ObsPathText.Text = report.ObsLogPath;
        MappingText.Text = $"GPU driver: {report.DetectedGpuDriver ?? "Unknown"} | Encoder: {report.DetectedEncoder ?? "Unknown"}";

        ObsFindingsList.ItemsSource = report.ObsLogFindings;
        EventsList.ItemsSource = report.WindowsEvents.Select(x => new
        {
            Header = $"[{x.Level}] {x.Source} - {x.Time:HH:mm:ss}",
            x.Message
        }).ToList();
    }

    private string GetSelectedProduct()
    {
        if (ProductCombo.SelectedItem is ComboBoxItem item && item.Content is string content)
        {
            return content;
        }

        return "OBS";
    }
}
