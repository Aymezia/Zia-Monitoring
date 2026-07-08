using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class MappingPage : Page
{
    public MappingPage()
    {
        InitializeComponent();
        DataContext = ((App)Microsoft.UI.Xaml.Application.Current).State;
        RefreshDisplays();
        RefreshControllers();
    }

    private void RefreshDisplays_Click(object sender, RoutedEventArgs e) => RefreshDisplays();

    private void RefreshDisplays()
    {
        DisplaysList.ItemsSource = Infrastructure.Collectors.RefreshRateDetector.DetectAll();
        MonitorDetailsList.ItemsSource = Infrastructure.Collectors.MonitorInfoCollector.DetectAll();
    }

    private void RefreshControllers_Click(object sender, RoutedEventArgs e) => RefreshControllers();

    private void RefreshControllers()
    {
        var controllers = ZiaMonitoring_App.Application.ControllerRadarService.Scan();
        ControllersList.ItemsSource = controllers;
        ControllersStatusLabel.Text = controllers.Count == 0
            ? "Aucune manette détectée."
            : $"{controllers.Count} manette(s) détectée(s).";
    }

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string uri && !string.IsNullOrWhiteSpace(uri))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Infrastructure.AppLog.Warn($"Lancement du jeu impossible: {uri}", ex);
            }
        }
    }
}
