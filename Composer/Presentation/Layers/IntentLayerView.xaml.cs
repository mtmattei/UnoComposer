using System.Collections;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class IntentLayerView : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public IntentLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Unloaded += (_, _) => DetachBindable();
        this.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible) Refresh();
        });
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        if (Visibility == Visibility.Visible) Refresh();
    }

    private void DetachBindable()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
    }

    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (Visibility == Visibility.Visible) Refresh();
        });
    }

    private void Refresh()
    {
        RefreshSelections();
    }

    private void OnIntentFieldTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (tb.FocusState == FocusState.Unfocused) return;
        MvuxCommandInvoker.Invoke(DataContext, "DismissExample");
        MvuxCommandInvoker.Invoke(DataContext, "MarkActiveDirty");
    }

    private void RefreshSelections()
    {
        var platforms = ReadValue("SelectedPlatforms");
        SetChip(WebChip,     platforms, PlatformKind.Web);
        SetChip(WindowsChip, platforms, PlatformKind.Windows);
        SetChip(AndroidChip, platforms, PlatformKind.Android);
        SetChip(IOSChip,     platforms, PlatformKind.iOS);
        SetChip(DesktopChip, platforms, PlatformKind.Desktop);

        var runtime = ReadValue("SelectedRuntime");
        Net11Chip.IsSelected = runtime is RuntimeKind r11 && r11 == RuntimeKind.Net11;
        Net10Chip.IsSelected = runtime is RuntimeKind r10 && r10 == RuntimeKind.Net10;
        Net9Chip.IsSelected  = runtime is RuntimeKind r9  && r9  == RuntimeKind.Net9;
    }

    private static void SetChip(PlatformChip chip, object? platforms, PlatformKind kind)
    {
        var selected = false;
        if (platforms is IEnumerable e)
        {
            foreach (var item in e)
            {
                if (item is PlatformKind p && p == kind) { selected = true; break; }
            }
        }
        if (chip.IsSelected != selected) chip.IsSelected = selected;
    }

    private object? ReadValue(string propertyName)
        => _bindable?.GetType().GetProperty(propertyName)?.GetValue(_bindable);
}
