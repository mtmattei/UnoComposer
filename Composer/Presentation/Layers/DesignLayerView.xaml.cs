using System.Collections.Immutable;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class DesignLayerView : UserControl
{
    // Code-block syntax colors — match the dark code surface palette already
    // defined in Brushes.xaml (CodeKeyword / CodeString / CodeComment / etc.).
    // Resolved once on first use so we don't recreate brushes per Run.
    private static readonly SolidColorBrush TagBrush     = new(Color.FromArgb(0xFF, 0x7A, 0xB3, 0xDF));
    private static readonly SolidColorBrush AttrBrush    = new(Color.FromArgb(0xFF, 0xE2, 0x9B, 0x5C));
    private static readonly SolidColorBrush StringBrush  = new(Color.FromArgb(0xFF, 0xA4, 0xC9, 0x7D));
    private static readonly SolidColorBrush CommentBrush = new(Color.FromArgb(0xFF, 0x7A, 0x7E, 0x85));
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(0xFF, 0xE4, 0xE4, 0xE4));

    private INotifyPropertyChanged? _bindable;

    public DesignLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        // Re-refresh whenever this layer flips to Visible — see UxLayerView.
        this.RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityChanged);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        if (Visibility == Visibility.Visible) RebuildPaletteInlines();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachBindable();

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (Visibility == Visibility.Visible) RebuildPaletteInlines();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        if (Visibility == Visibility.Visible) RebuildPaletteInlines();
    }

    private void DetachBindable()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
    }

    // No PropertyName filter — DesignTokens updates fire under whatever
    // proxy property name MVUX assigns (issue #23264). Idempotent rebuild.
    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (Visibility == Visibility.Visible) RebuildPaletteInlines();
        });
    }

    private void RebuildPaletteInlines()
    {
        var tokens = ReadDesignTokens() ?? DesignTokens.Default;
        var intent = ReadIntent() ?? Intent.Example;
        var ctx = IntentContext.DeriveFrom(intent);
        var references = ReadReferenceScreenshots();

        // Sample copy in the type-scale demo + the primary-button label
        // derive from the active intent so the design system preview shows
        // the user's actual content vocabulary, not the field-service demo.
        ReferenceSourceText.Text = BuildReferenceSourceSummary(references);
        SampleHeading.Text = $"{ctx.EntityTitle} #4471 · sample {ctx.EntityNoun}";
        SampleBody.Text    = ctx.IsFieldService
            ? "Arriving in 22 min · awaiting parts confirmation"
            : $"Last {ctx.EntityNoun} captured 22 min ago · ready for review";
        SamplePrimaryButton.Content = ctx.IsFieldService
            ? "Dispatch now →"
            : $"Save {ctx.EntityNoun} →";
        UnoComponentsText.Text = BuildCanvasComponentSummary(ctx);
        RepeaterRecommendationText.Text = BuildRepeaterRecommendation(ctx);
        ButtonRecommendationText.Text = BuildButtonRecommendation(ctx);

        var light = tokens.AsLightPaletteTokens();
        var dark  = tokens.AsDarkPaletteTokens();

        PaletteXamlText.Inlines.Clear();

        // <ResourceDictionary ...>
        AppendTagOpen("ResourceDictionary");
        AppendLineBreak();
        AppendIndent(4);
        AppendXmlnsAttribute("xmlns", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        AppendLineBreak();
        AppendIndent(4);
        AppendXmlnsAttribute("xmlns:x", "http://schemas.microsoft.com/winfx/2006/xaml");
        AppendRun(">", TagBrush);
        AppendLineBreak();
        AppendLineBreak();

        // <ResourceDictionary.ThemeDictionaries>
        AppendIndent(2);
        AppendTagOpen("ResourceDictionary.ThemeDictionaries");
        AppendRun(">", TagBrush);
        AppendLineBreak();
        AppendLineBreak();

        AppendThemeDictionary("Light", light);
        AppendLineBreak();
        AppendThemeDictionary("Dark", dark);
        AppendLineBreak();

        AppendIndent(2);
        AppendTagClose("ResourceDictionary.ThemeDictionaries");
        AppendLineBreak();
        AppendLineBreak();

        AppendTagClose("ResourceDictionary");
    }

    private void AppendThemeDictionary(string key, ImmutableArray<DesignSwatch> tokens)
    {
        AppendIndent(4);
        AppendTagOpen("ResourceDictionary");
        AppendRun(" ", DefaultBrush);
        AppendRun("x:Key", AttrBrush);
        AppendRun("=", DefaultBrush);
        AppendRun($"\"{key}\"", StringBrush);
        AppendRun(">", TagBrush);
        AppendLineBreak();

        foreach (var s in tokens)
        {
            AppendIndent(6);
            AppendTagOpen("Color");
            AppendRun(" ", DefaultBrush);
            AppendRun("x:Key", AttrBrush);
            AppendRun("=", DefaultBrush);
            AppendRun($"\"{s.TokenKey}\"", StringBrush);
            AppendRun(">", TagBrush);
            AppendRun(s.Hex, DefaultBrush);
            AppendTagClose("Color");
            AppendLineBreak();
        }

        AppendIndent(4);
        AppendTagClose("ResourceDictionary");
        AppendLineBreak();
    }

    private DesignTokens ReadDesignTokens()
        => ProxyReader.ReadDesignTokens((object?)_bindable ?? this.DataContext);

    private Intent ReadIntent()
        => ProxyReader.ReadIntent((object?)_bindable ?? this.DataContext);

    private ImmutableArray<string> ReadReferenceScreenshots()
        => ProxyReader.ReadArray<string>((object?)_bindable ?? this.DataContext, "ReferenceScreenshotPaths");

    private static string BuildReferenceSourceSummary(ImmutableArray<string> references)
    {
        if (references.IsDefaultOrEmpty)
            return "No reference screenshots are attached. This design system is currently derived from intent fields and selected tokens.";

        var noun = references.Length == 1 ? "reference" : "references";
        return $"Reference-driven: {references.Length} uploaded {noun} from the intent stage seed UX flow and design-token generation after the overview is accepted.";
    }

    private static string BuildCanvasComponentSummary(IntentContext ctx)
    {
        var listSurface = ctx.IsFieldService ? "assignment rows" : $"{ctx.EntityNoun} rows";
        return $"Prioritize Uno.Toolkit surfaces: AutoLayout for spacing, ItemsRepeater for {listSurface}, Toolkit/Material Button styles for primary actions, InfoBar for offline/error feedback, and chip/segmented controls for filters and modes.";
    }

    private static string BuildRepeaterRecommendation(IntentContext ctx)
        => ctx.IsFieldService
            ? "Virtualized assignment queue rows and dense scan surfaces."
            : $"Virtualized {ctx.EntityNoun} list rows and dense scan surfaces.";

    private static string BuildButtonRecommendation(IntentContext ctx)
        => ctx.IsFieldService
            ? "Primary dispatch, save, and queue actions using Uno.Toolkit/Material styling."
            : $"Primary save and commit actions for {ctx.EntityNoun} detail using Uno.Toolkit/Material styling.";

    // ─── Inline emission helpers ──────────────────────────────────────────
    private void AppendRun(string text, SolidColorBrush brush)
        => PaletteXamlText.Inlines.Add(new Run { Text = text, Foreground = brush });

    private void AppendLineBreak()
        => PaletteXamlText.Inlines.Add(new LineBreak());

    private void AppendIndent(int spaces)
        => AppendRun(new string(' ', spaces), DefaultBrush);

    private void AppendTagOpen(string name)
    {
        AppendRun("<", TagBrush);
        AppendRun(name, TagBrush);
    }

    private void AppendTagClose(string name)
    {
        AppendRun("</", TagBrush);
        AppendRun(name, TagBrush);
        AppendRun(">", TagBrush);
    }

    private void AppendXmlnsAttribute(string name, string value)
    {
        AppendRun(name, AttrBrush);
        AppendRun("=", DefaultBrush);
        AppendRun($"\"{value}\"", StringBrush);
    }
}
