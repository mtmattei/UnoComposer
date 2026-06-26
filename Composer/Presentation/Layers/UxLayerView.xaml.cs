using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class UxLayerView : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public UxLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        // Re-run RefreshFlow whenever the layer flips to Visible. The
        // CompositionViewModel's top-level mirrors (AppType, etc.) don't
        // reliably raise PropertyChanged through the bindable proxy for our
        // subscription while the control sits Collapsed — but `ProxyReader`
        // can always read the current values synchronously, so refreshing
        // on activation guarantees the cards reflect the latest Intent.
        this.RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityChanged);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        if (Visibility == Visibility.Visible) RefreshFlow();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachBindable();

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (Visibility == Visibility.Visible) RefreshFlow();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        if (Visibility == Visibility.Visible) RefreshFlow();
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
            if (Visibility == Visibility.Visible) RefreshFlow();
        });
    }

    // The UX flow markdown emits one screen per line as "N. **Label** — Caption".
    // Parsed back out so prompt refinements (which rewrite the markdown) are
    // reflected in the canvas cards, not just the right-rail file view.
    private static readonly Regex ScreenLineRegex =
        new(@"^\s*\d+\.\s+\*\*(?<label>.+?)\*\*\s*[—–-]\s*(?<cap>.+?)\s*$", RegexOptions.Multiline);

    private void RefreshFlow()
    {
        var intent = ReadIntent() ?? Intent.Example;
        var ctx = IntentContext.DeriveFrom(intent);

        // Prefer the refined override markdown so prompt edits like "add a
        // settings page" change the visible flow; fall back to the intent-
        // derived ScreenFlow when this layer hasn't been refined yet.
        var screens = ParseRefinedScreens() ?? ctx.ScreenFlow;

        SetCard(Card0Title, Card0Caption, screens, 0);
        SetCard(Card1Title, Card1Caption, screens, 1);
        SetCard(Card2Title, Card2Caption, screens, 2);
        SetCard(Card3Title, Card3Caption, screens, 3);
        SetCard(Card4Title, Card4Caption, screens, 4);

        FilenameText.Text     = ctx.UxFlowFilename;
        FlowSummaryText.Text  = $"Five screens for the primary {ctx.EntityNoun} flow. Arrows show sequence.";
    }

    // Reads the active UX layer's refined override markdown (if any) and parses
    // the screen list. Returns null when this layer has no override yet or the
    // markdown doesn't contain a recognizable screen list — caller then falls
    // back to the intent-derived ScreenFlow.
    private ImmutableArray<UxScreen>? ParseRefinedScreens()
    {
        var active = ProxyReader.Read<LayerStatus>((object?)_bindable ?? this.DataContext, "ActiveLayer");
        if (active is null || !string.Equals(active.Id, "ux", StringComparison.Ordinal))
            return null;

        var md = active.OverrideMarkdown;
        if (string.IsNullOrWhiteSpace(md))
            return null;

        var matches = ScreenLineRegex.Matches(md);
        if (matches.Count == 0)
            return null;

        var builder = ImmutableArray.CreateBuilder<UxScreen>();
        foreach (Match m in matches)
            builder.Add(new UxScreen(m.Groups["label"].Value.Trim(), m.Groups["cap"].Value.Trim()));
        return builder.ToImmutable();
    }

    private static void SetCard(TextBlock titleBlock, TextBlock captionBlock, System.Collections.Immutable.ImmutableArray<UxScreen> flow, int idx)
    {
        var screen = idx < flow.Length ? flow[idx] : new UxScreen(string.Empty, string.Empty);
        titleBlock.Text   = screen.Name.ToUpperInvariant();
        captionBlock.Text = screen.Caption;
    }

    private Intent ReadIntent()
        => ProxyReader.ReadIntent((object?)_bindable ?? this.DataContext);
}
