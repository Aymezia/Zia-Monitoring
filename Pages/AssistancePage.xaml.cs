using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class AssistancePage : Page
{
    private readonly App _app;

    public AssistancePage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        DataContext = _app.State;
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        var zipPath = _app.SupportExportService.ExportBundle(_app.State);
        await ShowMessageAsync("Rapport diagnostic", $"Export termine.{Environment.NewLine}{zipPath}");
    }

    private async void AssistedSession_Click(object sender, RoutedEventArgs e)
    {
        await ShowMessageAsync("Session assistee", "Demande enregistree: intervention assistee.");
    }

    private async void PremiumSession_Click(object sender, RoutedEventArgs e)
    {
        await ShowMessageAsync("Session premium", "Demande enregistree: session premium avec suivi avant/apres.");
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
