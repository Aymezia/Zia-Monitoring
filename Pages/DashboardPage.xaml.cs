using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace ZiaMonitoring_App.Pages;

public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        InitializeComponent();
        DataContext = ((App)Microsoft.UI.Xaml.Application.Current).State;

        // Animations d'ambiance "console live" : opacité uniquement (composée
        // par le GPU), coût négligeable même en continu.
        AnimateOpacity(LiveDot, from: 1.0, to: 0.25, ms: 1000);
        AnimateOpacity(CursorBlock, from: 1.0, to: 0.0, ms: 500);
    }

    private static void AnimateOpacity(UIElement element, double from, double to, double ms)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(ms)),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
