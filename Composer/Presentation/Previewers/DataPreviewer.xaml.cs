using System.ComponentModel;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Previewers;

public sealed partial class DataPreviewer : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public DataPreviewer()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += (_, _) => DetachBindable();
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

        Entity1Block.Text =
            $"public record {ctx.EntityTitle}(\n" +
            $"  string Id,\n" +
            $"  string Title,\n" +
            $"  DateTime Scheduled,\n" +
            $"  {ctx.EntityTitle}Status Status);";

        Entity2Block.Text =
            $"public record {ctx.UserSingularTitle}(\n" +
            $"  string Id,\n" +
            $"  string Name,\n" +
            $"  bool Available);";

        ScheduleBlock.Text =
            $"public record Schedule(\n" +
            $"  string {ctx.EntityTitle}Id,\n" +
            $"  string {ctx.UserSingularTitle}Id,\n" +
            $"  TimeSlot Slot);";

        WhyTheseShapesText.Text = $"Records, not classes — immutable by default. {ctx.EntityTitle} is the spine; everything else references it.";
    }

    private Intent ReadIntent() => ProxyReader.ReadIntent(_bindable);
}
