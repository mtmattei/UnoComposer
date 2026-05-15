using System.ComponentModel;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class InteractionsLayerView : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public InteractionsLayerView()
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

        // Default-state annotation — uses ctx.DefaultStateLabel (already
        // entity-aware) and ctx.UserSingular for the offline-first
        // reasoning so the prose matches the active intent.
        DefaultStateAnnotation.Text = ctx.IsOfflineFirst
            ? $"{ctx.DefaultStateLabel} Offline-first means we always show a sync-pending banner instead of an error if data fails to load — a {ctx.UserSingular} shouldn't have to wait for a network round-trip to start their day."
            : $"{ctx.DefaultStateLabel} Live data — show a skeleton while loading, never a spinner-on-blank.";
    }

    private Intent ReadIntent() => ProxyReader.ReadIntent(_bindable);
}
