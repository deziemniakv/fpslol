using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FpsLol.Models;

namespace FpsLol.Controls;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && (v == Visibility.Visible) != Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is not null && (value is not string s || s.Length > 0);
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var visible = value is int n && n > 0;
        if (Invert) visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>Maps semantic states (OperationStatus, RiskLevel, "tone" strings) to status brushes.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    public bool Soft { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var tone = value switch
        {
            OperationStatus.Success => "success",
            OperationStatus.Failed => "danger",
            OperationStatus.Skipped => "warning",
            OperationStatus.NotSupported => "neutral",
            RiskLevel.Safe => "success",
            RiskLevel.Moderate => "warning",
            RiskLevel.Advanced => "danger",
            bool b => b ? "success" : "warning",
            string s => s.ToLowerInvariant(),
            _ => "neutral",
        };
        var key = (tone, Soft) switch
        {
            ("success", false) => "Brush.Success",
            ("warning", false) => "Brush.Warning",
            ("danger", false) => "Brush.Danger",
            ("info", false) => "Brush.Info",
            ("success", true) => "Brush.SuccessSoft",
            ("warning", true) => "Brush.WarningSoft",
            ("danger", true) => "Brush.DangerSoft",
            ("info", true) => "Brush.InfoSoft",
            (_, true) => "Brush.NeutralSoft",
            _ => "Brush.TextSecondary",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Picks an icon geometry resource for an OperationStatus.</summary>
public sealed class StatusIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value switch
        {
            OperationStatus.Success => "Icon.CircleCheck",
            OperationStatus.Failed => "Icon.CircleX",
            OperationStatus.Skipped => "Icon.CircleMinus",
            _ => "Icon.CircleSlash",
        };
        return Application.Current.TryFindResource(key)!;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Resolves a resource key string (e.g. "Icon.Cpu") to the resource.</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.TryParse(targetType, parameter.ToString(), out var r) ? r! : parameter : Binding.DoNothing;
}

/// <summary>Visible when the value equals the parameter; the parameter may list alternatives separated by '|'.</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString();
        var options = (parameter?.ToString() ?? string.Empty).Split('|');
        return options.Any(o => string.Equals(text, o, StringComparison.OrdinalIgnoreCase)) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
