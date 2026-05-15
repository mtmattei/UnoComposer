using System;
using System.Collections;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Composer.Models;

namespace Composer.Presentation.Controls;

// Right-rail coverage indicator. Reads ActiveLayerCoverage off the MVUX
// bindable proxy on every PropertyChanged tick (same pattern the footer
// uses) and renders a counter + dot strip + a first-3-chips row that
// invokes RefineSection on click. Hidden entirely when the active layer
// has no required sections (only "scaffold" today).
public sealed partial class CoverageBadge : UserControl
{
    private const int MaxInlineChips = 3;

    private INotifyPropertyChanged? _bindable;
    private string? _lastSignature;

    public CoverageBadge()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        Detach();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        Refresh();
    }

    private void Detach()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
    }

    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(Refresh);
    }

    private void Refresh()
    {
        var coverage = ProxyReader.Read<LayerCoverage>(_bindable, "ActiveLayerCoverage");
        if (coverage is null || coverage.Total == 0)
        {
            HostGrid.Visibility = Visibility.Collapsed;
            _lastSignature = null;
            return;
        }

        // Signature avoids rebuilding the dot list / chip row on every poll
        // tick — only when the actual coverage changed.
        var signature = $"{coverage.LayerId}|{coverage.Total}|{coverage.Covered}|" +
                        string.Join(",", coverage.Sections.Select(s => $"{s.Section.Heading}:{(s.Present && s.HasContent ? 1 : 0)}"));
        if (string.Equals(signature, _lastSignature, StringComparison.Ordinal)) return;
        _lastSignature = signature;

        HostGrid.Visibility = Visibility.Visible;
        CounterText.Text = $"{coverage.Covered} / {coverage.Total} sections covered";

        RebuildDots(coverage);
        RebuildChips(coverage);
    }

    private void RebuildDots(LayerCoverage coverage)
    {
        var items = new object[coverage.Sections.Length];
        for (int i = 0; i < coverage.Sections.Length; i++)
        {
            var s = coverage.Sections[i];
            var filled = s.Present && s.HasContent;
            var brush = (Brush)Application.Current.Resources[filled ? "ActionBrush" : "HairlineStrongBrush"];
            var dot = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = brush,
            };
            ToolTipService.SetToolTip(dot, $"{s.Section.Heading} — {s.Section.Purpose}");
            items[i] = dot;
        }
        DotsHost.ItemsSource = items;
    }

    private void RebuildChips(LayerCoverage coverage)
    {
        ChipsRow.Children.Clear();
        if (coverage.IsComplete)
        {
            ChipsRow.Visibility = Visibility.Collapsed;
            return;
        }

        ChipsRow.Visibility = Visibility.Visible;
        var chipStyle = Application.Current.Resources["SuggestionChipStyle"] as Style;
        var refineCmd = ProxyReader.IsCommandExecuting(_bindable, "RefineSection")
            ? null
            : (_bindable?.GetType().GetProperty("RefineSection")?.GetValue(_bindable) as ICommand);

        var missing = coverage.Missing;
        var inline = Math.Min(MaxInlineChips, missing.Length);
        for (int i = 0; i < inline; i++)
        {
            ChipsRow.Children.Add(BuildChip(missing[i].CoveragePrompt, chipStyle, refineCmd));
        }

        if (missing.Length > inline)
        {
            var rest = ImmutableArray.CreateRange(missing.Skip(inline));
            ChipsRow.Children.Add(BuildOverflow(rest, chipStyle, refineCmd));
        }
    }

    private static Button BuildChip(string label, Style? style, ICommand? cmd)
    {
        return new Button
        {
            Style = style,
            Content = label,
            Command = cmd,
            CommandParameter = label,
        };
    }

    private static Button BuildOverflow(ImmutableArray<SectionSpec> rest, Style? style, ICommand? cmd)
    {
        var flyoutPanel = new StackPanel { Spacing = 4, Padding = new Thickness(4) };
        foreach (var spec in rest)
        {
            var btn = new Button
            {
                Style = (Style?)Application.Current.Resources["LinkButtonStyle"],
                Content = spec.CoveragePrompt,
                Command = cmd,
                CommandParameter = spec.CoveragePrompt,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            flyoutPanel.Children.Add(btn);
        }
        var flyout = new Flyout { Content = flyoutPanel };
        return new Button
        {
            Style = style,
            Content = $"+{rest.Length} more",
            Flyout = flyout,
        };
    }
}
