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

    private async void RefreshHardwareDiag_Click(object sender, RoutedEventArgs e)
    {
        RefreshHardwareDiagButton.IsEnabled = false;
        HardwareDiagStatusLabel.Text = "Analyse en cours…";
        try
        {
            var (memory, pcie, ssd) = await Task.Run(() =>
                (ZiaMonitoring_App.Application.MemoryConfigDiagnosticsService.Analyze(),
                 ZiaMonitoring_App.Application.PcieLinkService.Detect(),
                 ZiaMonitoring_App.Application.SsdWearService.Analyze()));

            MemorySpeedLabel.Text = memory.SpeedLabel;
            MemoryChannelLabel.Text = memory.Channel.Label;
            PcieLinkList.ItemsSource = pcie;
            SsdWearList.ItemsSource = ssd;
            HardwareDiagStatusLabel.Text = pcie.Count == 0
                ? "Lien PCIe non disponible (GPU non-NVIDIA ou pilote absent)."
                : $"{pcie.Count} GPU analysé(s), {ssd.Count} disque(s) SSD/SCM analysé(s).";
        }
        finally
        {
            RefreshHardwareDiagButton.IsEnabled = true;
        }
    }

    private async void RefreshBattery_Click(object sender, RoutedEventArgs e)
    {
        RefreshBatteryButton.IsEnabled = false;
        BatteryStatusLabel.Text = "Génération du rapport batterie…";
        try
        {
            var batteries = await Task.Run(ZiaMonitoring_App.Application.BatteryService.GetBatteryHealth);
            BatteryList.ItemsSource = batteries;
            BatteryStatusLabel.Text = batteries.Count == 0
                ? "Aucune batterie détectée (PC fixe)."
                : $"{batteries.Count} batterie(s) analysée(s).";
        }
        finally
        {
            RefreshBatteryButton.IsEnabled = true;
        }
    }

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
