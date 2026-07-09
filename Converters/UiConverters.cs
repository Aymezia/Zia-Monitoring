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
