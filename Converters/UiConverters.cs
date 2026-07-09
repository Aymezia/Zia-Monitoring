using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using ZiaMonitoring_App.Application;

namespace ZiaMonitoring_App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;
        if (Invert)
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>true → vert, false → rouge (états OK/exposé).</summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ok = value is bool b && b;
        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[ok ? "ZiaGreenBrush" : "ZiaRedBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Low → vert, Medium → ambre, High (ou inconnu) → rouge.</summary>
public sealed class RiskLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = (value as string)?.ToLowerInvariant() switch
        {
            "low" => "ZiaGreenBrush",
            "medium" => "ZiaAmberBrush",
            _ => "ZiaRedBrush"
        };

        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>
/// Pourcentage 0-100 → largeur en pixels, à la même échelle que le
/// MappingMode="Absolute" de ZiaThermalBrush : révèle une portion du
/// dégradé thermique fixe plutôt que de l'étirer (repro de l'effet
/// "overflow:hidden" du web). ConverterParameter = largeur pleine échelle.
/// </summary>
public sealed class PercentToPixelWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var percent = value switch
        {
            double d => d,
            int i => i,
            _ => 0d
        };
        var fullWidth = parameter is string s && double.TryParse(s, out var w) ? w : 160d;
        return Math.Clamp(percent, 0, 100) / 100.0 * fullWidth;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Critical → rouge, Warning → ambre, Info → violet (constats d'audit PC).</summary>
public sealed class AuditSeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is AuditSeverity severity
            ? severity switch
            {
                AuditSeverity.Critical => "ZiaRedBrush",
                AuditSeverity.Warning => "ZiaAmberBrush",
                _ => "ZiaVioletBrush"
            }
            : "ZiaMutedBrush";

        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
