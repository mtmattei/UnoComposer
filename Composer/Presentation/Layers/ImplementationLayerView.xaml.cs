using System.ComponentModel;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class ImplementationLayerView : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public ImplementationLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += (_, _) => DetachBindable();
        // Re-refresh whenever this layer flips to Visible — see UxLayerView.
        this.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible) Refresh();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        if (Visibility == Visibility.Visible) Refresh();
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
        var intent = ReadIntent() ?? Intent.Example;
        var ctx = IntentContext.DeriveFrom(intent);

        RecapText.Text = $"↳ {ctx.EntityTitle} + {ctx.UserSingularTitle} + Schedule — sketched with explicit nullability.";

        // P4 screens line — list the 5 screens from the vibe-aware ScreenFlow
        // so the implementation plan matches the UX flow we already derived.
        var names = ctx.ScreenFlow.Select(s => s.Name).ToArray();
        ScreensListText.Text = $"{string.Join(", ", names)}. AutoLayout grids.";
    }

    private Intent ReadIntent() => ProxyReader.ReadIntent(_bindable);
}
