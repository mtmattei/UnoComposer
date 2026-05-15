using System.Collections.Immutable;
using System.ComponentModel;
using Composer.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Composer.Presentation.Controls;

public sealed partial class ReferenceScreenshotPanel : UserControl
{
    private INotifyPropertyChanged? _bindable;
    private string? _lastFrameSignature;

    public ReferenceScreenshotPanel()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachBindable(DataContext);
        RefreshPlatformFrames();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachBindable();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        AttachBindable(args.NewValue);
        RefreshPlatformFrames();
    }

    private void AttachBindable(object? source)
    {
        if (source is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
    }

    private void DetachBindable()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
    }

    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "SelectedPlatforms" or null)
            DispatcherQueue?.TryEnqueue(RefreshPlatformFrames);
    }

    private void RefreshPlatformFrames()
    {
        var platforms = ProxyReader.ReadHashSet<PlatformKind>(DataContext, "SelectedPlatforms");
        var ordered = platforms.OrderBy(p => (int)p).ToImmutableArray();
        var signature = string.Join(",", ordered.Select(p => p.ToString()));
        if (string.Equals(signature, _lastFrameSignature, StringComparison.Ordinal)) return;
        _lastFrameSignature = signature;

        PlatformFrameHost.Children.Clear();
        DefaultReferencesContent.Visibility = ordered.IsDefaultOrEmpty ? Visibility.Visible : Visibility.Collapsed;
        PlatformReferencesContent.Visibility = ordered.IsDefaultOrEmpty ? Visibility.Collapsed : Visibility.Visible;

        foreach (var platform in ordered)
            PlatformFrameHost.Children.Add(BuildPlatformFrame(platform));
    }

    private FrameworkElement BuildPlatformFrame(PlatformKind platform)
    {
        var isMobile = platform is PlatformKind.Android or PlatformKind.iOS;
        return isMobile ? BuildMobileFrame(platform) : BuildDesktopFrame(platform);
    }

    private FrameworkElement BuildMobileFrame(PlatformKind platform)
    {
        var root = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(new Border
        {
            Width = 78,
            Height = 136,
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("HairlineStrongBrush"),
            Background = Brush("NotepadSurfaceBrush"),
            Padding = new Thickness(8),
            Child = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition { Height = new GridLength(12) },
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
                    new RowDefinition { Height = new GridLength(18) },
                },
                Children =
                {
                    new Border
                    {
                        Width = 26,
                        Height = 3,
                        CornerRadius = new CornerRadius(2),
                        Background = Brush("Ink4Brush"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    MobilePlaceholderBody(),
                },
            },
        });
        root.Children.Add(FrameLabel(platform));
        return root;
    }

    private static FrameworkElement MobilePlaceholderBody()
    {
        var body = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(Line(42, 7));
        body.Children.Add(Line(52, 7));
        body.Children.Add(Line(34, 7));
        Grid.SetRow(body, 1);
        return body;
    }

    private FrameworkElement BuildDesktopFrame(PlatformKind platform)
    {
        var root = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var chrome = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(16) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        chrome.Children.Add(new Border
        {
            Background = Brush("PanelBrush"),
            BorderBrush = Brush("HairlineBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    Dot(), Dot(), Dot(),
                },
            },
        });
        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(34) },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };
        body.Children.Add(new Border { Background = Brush("PanelBrush"), Opacity = 0.68 });
        var stack = new StackPanel { Spacing = 7, Margin = new Thickness(10, 11, 10, 0) };
        stack.Children.Add(Line(86, 8));
        stack.Children.Add(Line(66, 8));
        stack.Children.Add(Line(92, 20));
        Grid.SetColumn(stack, 1);
        body.Children.Add(stack);
        Grid.SetRow(body, 1);
        chrome.Children.Add(body);

        root.Children.Add(new Border
        {
            Width = 148,
            Height = 100,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("HairlineStrongBrush"),
            Background = Brush("NotepadSurfaceBrush"),
            Child = chrome,
        });
        root.Children.Add(FrameLabel(platform));
        return root;
    }

    private static Border Dot()
        => new()
        {
            Width = 4,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Brush("Ink4Brush"),
        };

    private static Border Line(double width, double height)
        => new()
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(Math.Min(height / 2, 4)),
            Background = Brush("HairlineBrush"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    private static TextBlock FrameLabel(PlatformKind platform)
        => new()
        {
            Text = platform.DisplayName().ToUpperInvariant(),
            Style = (Style)Application.Current.Resources["EyebrowTinyText"],
            Foreground = Brush("Ink4Brush"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

    private static Brush Brush(string key)
        => (Brush)Application.Current.Resources[key];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is StorageFile file && !string.IsNullOrEmpty(file.Path))
                    MvuxCommandInvoker.Invoke(DataContext, "AddReferenceScreenshot", file.Path);
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reference screenshot drop failed: {ex}");
        }
    }

    private async void OnUploadImagesClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".gif");

#if HAS_UNO
            if (Microsoft.UI.Xaml.Window.Current is { } window)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
            }
#endif

            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(file.Path))
                    MvuxCommandInvoker.Invoke(DataContext, "AddReferenceScreenshot", file.Path);
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reference screenshot picker failed: {ex}");
        }
    }
}
