using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Previewers;

public sealed partial class UxPreviewer : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public UxPreviewer()
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
        var intent = ReadIntent() ?? Intent.Example;
        var ctx = IntentContext.DeriveFrom(intent);
        var screens = ctx.ScreenFlow;

        FlowEyebrow.Text = $"{ctx.EntityTitle} flow".ToUpperInvariant();

        SetRow(Row0Name, Row0Caption, screens, 0);
        SetRow(Row1Name, Row1Caption, screens, 1);
        SetRow(Row2Name, Row2Caption, screens, 2);
        SetRow(Row3Name, Row3Caption, screens, 3);
        SetRow(Row4Name, Row4Caption, screens, 4);

        WhyThisFlowText.Text = ctx.UxFlowRationale;
    }

    private static void SetRow(TextBlock nameBlock, TextBlock captionBlock, ImmutableArray<UxScreen> flow, int idx)
    {
        var screen = idx < flow.Length ? flow[idx] : new UxScreen(string.Empty, string.Empty);
        nameBlock.Text    = screen.Name;
        captionBlock.Text = screen.Caption;
    }

    private Intent ReadIntent() => ProxyReader.ReadIntent(_bindable);
}
