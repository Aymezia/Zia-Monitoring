using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class MappingPage : Page
{
    private readonly App _app;
    private DispatcherTimer? _driftTimer;

    public MappingPage()
    {
        InitializeComponent();
        _app = (App)Microsoft.UI.Xaml.Application.Current;
        DataContext = _app.State;
        RefreshDisplays();
        RefreshControllers();
        Unloaded += (_, _) => StopDriftTimer();
    }

    private async void RefreshSteamLibraries_Click(object sender, RoutedEventArgs e)
    {
        RefreshSteamLibrariesButton.IsEnabled = false;
        SteamLibrariesStatusLabel.Text = "Analyse en cours…";
        try
        {
            var libraries = await Task.Run(() => _app.SteamLibrary.Scan());
            SteamLibrariesList.ItemsSource = libraries;
            SteamLibrariesStatusLabel.Text = libraries.Count == 0
                ? "Aucune bibliothèque Steam détectée (Steam non installé ?)."
                : $"{libraries.Count} bibliothèque(s), {libraries.Sum(l => l.Games.Count)} jeu(x) au total.";
        }
        finally
        {
            RefreshSteamLibrariesButton.IsEnabled = true;
        }
    }

    private async void RefreshBluetooth_Click(object sender, RoutedEventArgs e)
    {
        RefreshBluetoothButton.IsEnabled = false;
        BluetoothStatusLabel.Text = "Recherche des périphériques Bluetooth…";
        try
        {
            var devices = await _app.BluetoothBattery.ScanAsync();
            BluetoothList.ItemsSource = devices;
            var lowCount = devices.Count(d => d.IsLow);
            BluetoothStatusLabel.Text = devices.Count == 0
                ? "Aucun périphérique Bluetooth connecté détecté."
                : lowCount > 0
                    ? $"{devices.Count} périphérique(s), {lowCount} avec batterie faible."
                    : $"{devices.Count} périphérique(s) détecté(s).";
        }
        finally
        {
            RefreshBluetoothButton.IsEnabled = true;
        }
    }

    private void DriftTest_Toggled(object sender, RoutedEventArgs e)
    {
        if (DriftTestToggle.IsOn)
            StartDriftTimer();
        else
            StopDriftTimer();
    }

    private void StartDriftTimer()
    {
        _driftTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _driftTimer.Tick += DriftTimer_Tick;
        _driftTimer.Start();
    }

    private void StopDriftTimer()
    {
        if (_driftTimer is null)
            return;
        _driftTimer.Stop();
        _driftTimer.Tick -= DriftTimer_Tick;
    }

    private void DriftTimer_Tick(object? sender, object e)
    {
        var reading = ZiaMonitoring_App.Application.ControllerRadarService.ReadPrimaryStickPositions();
        if (reading is null)
        {
            DriftStatusLabel.Text = "Aucune manette XInput détectée.";
            return;
        }

        // Canvas 120px, centre à 60 ; le point fait 14px donc offset de 7.
        // L'axe Y est inversé (l'écran descend, le stick monte).
        PlaceDot(LeftStickDot, reading.LeftX, reading.LeftY);
        PlaceDot(RightStickDot, reading.RightX, reading.RightY);

        DriftStatusLabel.Text = ZiaMonitoring_App.Application.ControllerRadarService.HasDrift(reading)
            ? "⚠ Déviation détectée. Si vous ne touchez pas les sticks, c'est un signe de drift."
            : "Sticks au centre — aucun drift détecté.";
    }

    private static void PlaceDot(Microsoft.UI.Xaml.Shapes.Ellipse dot, double x, double y)
    {
        const double center = 60 - 7;
        const double radius = 46;
        Canvas.SetLeft(dot, center + x * radius);
        Canvas.SetTop(dot, center - y * radius);
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
