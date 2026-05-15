using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class DataLayerView : UserControl
{
    // Code-block syntax colors — match the dark code surface palette
    // already used by the design layer's ColorPaletteOverride block.
    private static readonly SolidColorBrush KeywordBrush = new(Color.FromArgb(0xFF, 0xE2, 0x9B, 0x5C));
    private static readonly SolidColorBrush TypeBrush    = new(Color.FromArgb(0xFF, 0x7A, 0xB3, 0xDF));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(0xFF, 0xE4, 0xE4, 0xE4));

    private INotifyPropertyChanged? _bindable;

    public DataLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        // Re-refresh whenever this layer flips to Visible — see UxLayerView
        // for the bindable-proxy subscription rationale.
        this.RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityChanged);
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

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachBindable();

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
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

        EntitySummaryText.Text = $"Three entities — {ctx.EntityTitle}, {ctx.UserSingularTitle}, Schedule — with explicit fields and nullability.";

        Entity1Title.Text = ctx.EntityTitle;
        Entity2Title.Text = ctx.UserSingularTitle;

        // Foreign-key + status field names derive from the entity nouns. For
        // a calorie tracker: userId : string?, status : MealStatus.
        Entity1Field2A.Text = "description : string?";
        Entity1Field3A.Text = $"{ctx.UserSingular}Id : string?";
        Entity1Field3B.Text = $"status : {ctx.EntityTitle}Status";

        // Schedule's list of the primary entity — "jobs : Job[]" → "meals : Meal[]".
        ScheduleEntityListField.Text = $"{ctx.EntityPlural} : {ctx.EntityTitle}[]";

        ModelFilenameText.Text = $"Models/{ctx.EntityTitle}.cs";
        BuildModelBody(ctx);
    }

    private void BuildModelBody(IntentContext ctx)
    {
        ModelBodyText.Inlines.Clear();

        // public partial record {EntityTitle}(
        Append("public partial record", KeywordBrush);
        Append(" " + ctx.EntityTitle + "(", DefaultBrush);
        LineBreak();

        AppendField("string",    "Id");
        AppendField("string",    "Title");
        AppendField("string?",   "Description");
        AppendField("DateTime?", "ScheduledAt");
        AppendField("string?",   ctx.UserSingularTitle + "Id");
        AppendField(ctx.EntityTitle + "Status", "Status", isType: false);
        AppendField("string?",   "Notes");
        AppendField("SyncState", "SyncState", isType: false, last: true);
    }

    private void AppendField(string type, string name, bool isType = true, bool last = false)
    {
        Append("    ", DefaultBrush);
        // "type" → colored as a CLR type for primitives; for custom types
        // (StatusEnum / SyncState) we keep them in the default ink.
        Append(type, isType ? TypeBrush : DefaultBrush);
        Append(" " + name + (last ? ");" : ","), DefaultBrush);
        LineBreak();
    }

    private void Append(string text, SolidColorBrush brush)
        => ModelBodyText.Inlines.Add(new Run { Text = text, Foreground = brush });

    private void LineBreak()
        => ModelBodyText.Inlines.Add(new LineBreak());

    private Intent ReadIntent() => ProxyReader.ReadIntent(_bindable);
}
