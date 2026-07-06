using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace ZiaMonitoring_App.Controls;

/// <summary>
/// Jauge circulaire légère (anneau + valeur centrée), 0–100 %.
/// Remplace les PieChart LiveCharts sur les tuiles KPI : pas de fond de
/// canvas, rendu 100 % XAML aux couleurs du thème.
/// </summary>
public sealed partial class CircularGauge : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CircularGauge),
        new PropertyMetadata(0d, OnValueChanged));

    private const double Center = 58;
    private const double Radius = 43;

    public CircularGauge()
    {
        InitializeComponent();
        UpdateArc();
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((CircularGauge)d).UpdateArc();

    private void UpdateArc()
    {
        var percent = Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 100);
        ValueText.Text = $"{percent:F0}%";

        // Un ArcSegment ne peut pas décrire un cercle complet : 359,9° max.
        var sweepDegrees = percent / 100 * 359.9;
        if (sweepDegrees < 0.5)
        {
            ArcPath.Data = null;
            return;
        }

        var startAngle = -Math.PI / 2; // départ en haut, sens horaire
        var endAngle = startAngle + sweepDegrees * Math.PI / 180;

        var start = new Point(Center + Radius * Math.Cos(startAngle), Center + Radius * Math.Sin(startAngle));
        var end = new Point(Center + Radius * Math.Cos(endAngle), Center + Radius * Math.Sin(endAngle));

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(Radius, Radius),
            IsLargeArc = sweepDegrees > 180,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ArcPath.Data = geometry;
    }
}
