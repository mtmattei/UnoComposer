using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Composer.Presentation;

public sealed class HexToBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> _cache = new();

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return new SolidColorBrush(Colors.Transparent);

        if (_cache.TryGetValue(hex, out var cached)) return cached;

        var brush = new SolidColorBrush(ParseHex(hex));
        _cache[hex] = brush;
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();

    private static Color ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 6)
        {
            return Color.FromArgb(0xFF,
                System.Convert.ToByte(s.Substring(0, 2), 16),
                System.Convert.ToByte(s.Substring(2, 2), 16),
                System.Convert.ToByte(s.Substring(4, 2), 16));
        }
        if (s.Length == 8)
        {
            return Color.FromArgb(
                System.Convert.ToByte(s.Substring(0, 2), 16),
                System.Convert.ToByte(s.Substring(2, 2), 16),
                System.Convert.ToByte(s.Substring(4, 2), 16),
                System.Convert.ToByte(s.Substring(6, 2), 16));
        }
        return Colors.Transparent;
    }
}

public sealed class FlowTabStyleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var match = value is int i && int.TryParse(parameter?.ToString(), out var p) && i == p;
        var key = match ? "AccentActionButtonStyle" : "SuggestionChipStyle";
        return Application.Current.Resources[key];
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => DependencyProperty.UnsetValue;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class LayerLifecycleToBorderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not LayerLifecycle l) return Application.Current.Resources["HairlineBrush"]!;
        return l switch
        {
            LayerLifecycle.Active => Application.Current.Resources["ActionBrush"]!,
            LayerLifecycle.Locked => Application.Current.Resources["Ink2Brush"]!,
            _ => new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class LifecycleIsLockedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is LayerLifecycle l && l == LayerLifecycle.Locked ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var hasContent = !string.IsNullOrEmpty(value as string);
        if (parameter is string s && s == "invert") hasContent = !hasContent;
        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class IndexToStepNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int i ? $"{(i + 1):D2}" : "01";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

// LayerStatus.GapCount → "(N gaps)" text or Visibility, depending on the
// ConverterParameter. ParameterParam "vis" returns Visibility; default
// returns the formatted string. Used by the LeftRail item template to render
// a quiet inline gap annotation on locked layers.
public sealed class GapCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var n = value switch
        {
            int i => i,
            _ => 0,
        };
        if (parameter is string s && s == "vis")
            return n > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (n <= 0) return string.Empty;
        return n == 1 ? "(1 gap)" : $"({n} gaps)";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class LayerStatusLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not LayerStatus layer) return "planned";
        return layer.Lifecycle switch
        {
            LayerLifecycle.Locked => layer.GapCount is > 0 ? $"{layer.GapCount} gaps" : "locked",
            LayerLifecycle.Active when layer.State == LayerState.Previewing => "preview",
            LayerLifecycle.Active when layer.State == LayerState.Dirty => "editing",
            LayerLifecycle.Active => "active",
            _ => "planned",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class LifecycleIsActiveConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is LayerLifecycle l && l == LayerLifecycle.Active ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
}

public sealed class LifecycleToIndexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not LayerLifecycle l)
            return Application.Current.Resources["Ink4Brush"]!;
        return l switch
        {
            LayerLifecycle.Active => Application.Current.Resources["InkBrush"]!,
            LayerLifecycle.Locked => Application.Current.Resources["Ink3Brush"]!,
            _                     => Application.Current.Resources["Ink4Brush"]!,
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class PlatformKindToDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is PlatformKind kind ? kind.DisplayName().ToUpperInvariant() : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class RuntimeKindToDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is RuntimeKind kind ? kind.DisplayName() : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class RuntimeKindToShortGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is RuntimeKind kind ? kind.ShortGlyph() : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class RuntimeKindIsPreviewToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is RuntimeKind kind && kind.IsPreview() ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class RuntimeKindIsLegacyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is RuntimeKind kind && kind.IsLegacy() ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class RuntimeKindIsNotLegacyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is RuntimeKind kind && !kind.IsLegacy() ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public sealed class FilePathToImageSourceConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var uri = path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                      || path.StartsWith("ms-appx:", StringComparison.OrdinalIgnoreCase)
                ? new Uri(path)
                : new Uri($"file:///{path.Replace('\\', '/')}");
            return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
