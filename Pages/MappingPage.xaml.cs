using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class MappingPage : Page
{
    public MappingPage()
    {
        InitializeComponent();
        DataContext = ((App)Microsoft.UI.Xaml.Application.Current).State;
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
