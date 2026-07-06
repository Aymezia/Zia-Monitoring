using Microsoft.UI.Xaml.Controls;

namespace ZiaMonitoring_App.Pages;

public sealed partial class NetworkPage : Page
{
    public NetworkPage()
    {
        InitializeComponent();
        DataContext = ((App)Microsoft.UI.Xaml.Application.Current).State;
    }
}
