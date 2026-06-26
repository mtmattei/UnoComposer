using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Composer.Presentation.Controls;

/// <summary>
/// Lightweight markdown preview for the right rail. It intentionally uses core
/// WinUI elements instead of MarkdownTextBlock so desktop rendering stays
/// deterministic across Uno targets. Supports headings, paragraphs, bullet and
/// numbered lists, fenced code, tables (rendered as stacked definition rows for
/// the narrow rail), blockquotes, horizontal rules, and inline bold / code /
/// links.
/// </summary>
public sealed partial class MarkdownPreviewer : UserControl
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownPreviewer),
            new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    // Memoization keys for SyncMarkdown — skip the full visual-tree rebuild when
    // neither the source markdown nor the theme has changed since the last render.
    private string? _renderedMarkdown;
    private ElementTheme? _renderedTheme;

    public MarkdownPreviewer()
    {
        this.InitializeComponent();
        this.Loaded += (_, _) => SyncMarkdown();
        this.ActualThemeChanged += (_, _) => SyncMarkdown();
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownPreviewer previewer)
            previewer.SyncMarkdown();
    }

    private void SyncMarkdown()
    {
        if (MarkdownStack is null) return;

        var markdown = Markdown ?? string.Empty;
        // Skip the Children.Clear() + full rebuild when neither the markdown nor
        // the theme changed since the last render. Callers already guard same-
        // content Markdown sets, but Loaded / ActualThemeChanged can re-enter
        // with identical inputs (e.g. control reloaded during navigation).
        if (string.Equals(_renderedMarkdown, markdown, StringComparison.Ordinal) &&
            _renderedTheme == ActualTheme)
            return;
        _renderedMarkdown = markdown;
        _renderedTheme = ActualTheme;

        MarkdownStack.Children.Clear();

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        var table = new List<string>();
        var quote = new List<string>();
        var inCode = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                FlushTable(table);
                FlushQuote(quote);
                if (inCode)
                {
                    AddCodeBlock(string.Join(Environment.NewLine, code));
                    code.Clear();
                }
                inCode = !inCode;
                continue;
            }

            if (inCode)
            {
                code.Add(raw);
                continue;
            }

            if (line.StartsWith("|", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                FlushQuote(quote);
                table.Add(line);
                continue;
            }
            FlushTable(table);

            if (line.StartsWith(">", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                quote.Add(line.TrimStart('>').TrimStart());
                continue;
            }
            FlushQuote(quote);

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(paragraph);
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal) ||
                line.StartsWith("## ", StringComparison.Ordinal) ||
                line.StartsWith("### ", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                AddHeading(line);
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) ||
                line.StartsWith("* ", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
                AddListItem("•", line[2..]);
                continue;
            }

            if (TryGetOrderedMarker(line, out var marker, out var rest))
            {
                FlushParagraph(paragraph);
                AddListItem(marker, rest);
                continue;
            }

            if (IsHorizontalRule(line))
            {
                FlushParagraph(paragraph);
                AddRule();
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph(paragraph);
        FlushTable(table);
        FlushQuote(quote);
        if (code.Count > 0)
            AddCodeBlock(string.Join(Environment.NewLine, code));

        if (MarkdownStack.Children.Count == 0)
            AddText("No markdown generated yet.", "BodySmallText", "Ink3Brush");
    }

    private static bool TryGetOrderedMarker(string line, out string marker, out string rest)
    {
        marker = string.Empty;
        rest = string.Empty;
        var dot = line.IndexOf(". ", StringComparison.Ordinal);
        if (dot is < 1 or > 3) return false;
        for (var i = 0; i < dot; i++)
        {
            if (!char.IsAsciiDigit(line[i])) return false;
        }
        marker = line[..(dot + 1)];
        rest = line[(dot + 2)..];
        return true;
    }

    private static bool IsHorizontalRule(string line)
    {
        var t = line.Trim();
        if (t.Length < 3) return false;
        var c = t[0];
        if (c is not ('-' or '*' or '_')) return false;
        foreach (var ch in t)
        {
            if (ch != c) return false;
        }
        return true;
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) return;
        AddText(string.Join(" ", paragraph), "BodySmallText", "Ink2Brush");
        paragraph.Clear();
    }

    private void FlushQuote(List<string> quote)
    {
        if (quote.Count == 0) return;
        var block = CreateTextBlock(string.Join(" ", quote), "AnnotationText", "Ink2Brush");
        MarkdownStack.Children.Add(new Border
        {
            BorderBrush = Brush("ActionBrush"),
            BorderThickness = new Thickness(2, 0, 0, 0),
            Padding = new Thickness(10, 1, 0, 1),
            Child = block
        });
        quote.Clear();
    }

    private void FlushTable(List<string> table)
    {
        if (table.Count == 0) return;

        var rows = table
            .Select(SplitTableRow)
            .Where(r => r.Length > 0)
            .ToList();
        table.Clear();

        var separatorIndex = rows.FindIndex(IsSeparatorRow);
        var header = separatorIndex > 0 ? rows[separatorIndex - 1] : null;
        var dataRows = rows
            .Where((r, i) => i != separatorIndex && (header is null || i != separatorIndex - 1))
            .ToList();
        if (dataRows.Count == 0) return;

        var host = new StackPanel { Spacing = 8 };
        for (var r = 0; r < dataRows.Count; r++)
        {
            if (r > 0)
                host.Children.Add(new Border { Height = 1, Background = Brush("HairlineBrush") });

            var cells = dataRows[r];
            var rowPanel = new StackPanel { Spacing = 2 };

            var title = CreateTextBlock(string.Empty, "LabelLargeText", "InkBrush");
            AppendInlines(title, cells[0]);
            rowPanel.Children.Add(title);

            for (var c = 1; c < cells.Length; c++)
            {
                if (string.IsNullOrWhiteSpace(cells[c])) continue;

                if (header is not null && c < header.Length && !string.IsNullOrWhiteSpace(header[c]))
                {
                    var caption = CreateTextBlock(header[c].ToUpperInvariant(), "EyebrowTinyText", "Ink3Brush");
                    caption.Margin = new Thickness(0, 4, 0, 0);
                    rowPanel.Children.Add(caption);
                }

                var value = CreateTextBlock(string.Empty, "BodySmallText", "Ink2Brush");
                AppendInlines(value, cells[c]);
                rowPanel.Children.Add(value);
            }

            host.Children.Add(rowPanel);
        }

        MarkdownStack.Children.Add(host);
    }

    private static string[] SplitTableRow(string line)
        => line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    private static bool IsSeparatorRow(string[] cells)
        => cells.All(c => c.Length > 0 && c.Trim(':').All(ch => ch == '-'));

    private void AddListItem(string marker, string text)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var markerBlock = CreateTextBlock(marker, "BodySmallText", "Ink3Brush");
        markerBlock.Margin = new Thickness(0, 0, 6, 0);
        markerBlock.MinWidth = 14;
        Grid.SetColumn(markerBlock, 0);
        grid.Children.Add(markerBlock);

        var content = CreateTextBlock(string.Empty, "BodySmallText", "Ink2Brush");
        AppendInlines(content, text);
        Grid.SetColumn(content, 1);
        grid.Children.Add(content);

        MarkdownStack.Children.Add(grid);
    }

    private void AddRule()
        => MarkdownStack.Children.Add(new Border
        {
            Height = 1,
            Background = Brush("HairlineBrush"),
            Margin = new Thickness(0, 4, 0, 4)
        });

    private void AddHeading(string line)
    {
        var level = line.StartsWith("### ", StringComparison.Ordinal) ? 3
            : line.StartsWith("## ", StringComparison.Ordinal) ? 2
            : 1;
        var block = CreateTextBlock(string.Empty, level == 1 ? "HeadlineSmallText" : "LabelLargeText", "InkBrush");
        AppendInlines(block, line[(level + 1)..]);
        if (level > 1)
            block.Margin = new Thickness(0, 8, 0, 0);
        MarkdownStack.Children.Add(block);
    }

    private void AddCodeBlock(string text)
    {
        var block = CreateTextBlock(text.TrimEnd(), "MonoBodyText", "CodeDarkForegroundBrush");

        MarkdownStack.Children.Add(new Border
        {
            Background = Brush("CodeDarkSurfaceBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Child = block
        });
    }

    private void AddText(string text, string styleKey, string brushKey)
    {
        var block = CreateTextBlock(string.Empty, styleKey, brushKey);
        AppendInlines(block, text);
        MarkdownStack.Children.Add(block);
    }

    private TextBlock CreateTextBlock(string text, string styleKey, string brushKey)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources[styleKey],
            Foreground = Brush(brushKey)
        };
    }

    /// <summary>
    /// Appends <paramref name="text"/> to the TextBlock as styled inline runs:
    /// **bold**, `code`, and [label](http-url). Unmatched markers render
    /// literally. No nesting — code spans win, then links, then bold.
    /// </summary>
    private void AppendInlines(TextBlock target, string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var codeStart = FindSpan(text, i, "`", "`", out var codeEnd);
            var boldStart = FindSpan(text, i, "**", "**", out var boldEnd);
            var linkStart = FindLink(text, i, out var label, out var url, out var linkEnd);

            var next = MinIndex(codeStart, boldStart, linkStart);
            if (next < 0)
            {
                target.Inlines.Add(new Run { Text = text[i..] });
                break;
            }

            if (next > i)
                target.Inlines.Add(new Run { Text = text[i..next] });

            if (next == codeStart)
            {
                target.Inlines.Add(new Run
                {
                    Text = text[(codeStart + 1)..codeEnd],
                    FontFamily = MonoFont,
                    FontSize = Math.Max(target.FontSize - 1, 10),
                    Foreground = Brush("InkBrush")
                });
                i = codeEnd + 1;
            }
            else if (next == linkStart)
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink { NavigateUri = uri, Foreground = Brush("ActionBrush") };
                    link.Inlines.Add(new Run { Text = label });
                    target.Inlines.Add(link);
                }
                else
                {
                    target.Inlines.Add(new Run { Text = label });
                }
                i = linkEnd + 1;
            }
            else
            {
                target.Inlines.Add(new Run
                {
                    Text = text[(boldStart + 2)..boldEnd],
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("InkBrush")
                });
                i = boldEnd + 2;
            }
        }
    }

    /// <summary>Index of an opener that also has a closer; -1 otherwise. Out: closer index.</summary>
    private static int FindSpan(string text, int from, string open, string close, out int closeIndex)
    {
        closeIndex = -1;
        var start = text.IndexOf(open, from, StringComparison.Ordinal);
        if (start < 0) return -1;
        var end = text.IndexOf(close, start + open.Length, StringComparison.Ordinal);
        if (end < 0 || end == start + open.Length) return -1;
        closeIndex = end;
        return start;
    }

    /// <summary>Index of a [label](url) opener with a complete closer; -1 otherwise.</summary>
    private static int FindLink(string text, int from, out string label, out string url, out int endIndex)
    {
        label = string.Empty;
        url = string.Empty;
        endIndex = -1;
        var open = text.IndexOf('[', from);
        while (open >= 0)
        {
            var mid = text.IndexOf("](", open + 1, StringComparison.Ordinal);
            if (mid < 0) return -1;
            var close = text.IndexOf(')', mid + 2);
            if (close < 0) return -1;
            label = text[(open + 1)..mid];
            url = text[(mid + 2)..close];
            if (!label.Contains('[') && !label.Contains(']'))
            {
                endIndex = close;
                return open;
            }
            open = text.IndexOf('[', open + 1);
        }
        return -1;
    }

    private static int MinIndex(params int[] indexes)
    {
        var min = -1;
        foreach (var idx in indexes)
        {
            if (idx >= 0 && (min < 0 || idx < min)) min = idx;
        }
        return min;
    }

    private FontFamily MonoFont
        => (FontFamily)Application.Current.Resources["MonoFontFamily"];

    // App brushes live in ThemeDictionaries; the app-level indexer resolves
    // them against the app theme, not this element's ActualTheme, and the
    // returned instance never re-themes. Resolve from the matching theme
    // dictionary so rebuilt blocks pick up the active theme.
    private Brush Brush(string key)
    {
        var themeKey = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        if (FindThemeDictionary(Application.Current.Resources, themeKey) is { } themed &&
            themed.TryGetValue(key, out var value) && value is Brush brush)
        {
            return brush;
        }
        return (Brush)Application.Current.Resources[key];
    }

    private static ResourceDictionary? FindThemeDictionary(ResourceDictionary root, string themeKey)
    {
        if (root.ThemeDictionaries.TryGetValue(themeKey, out var direct) && direct is ResourceDictionary hit)
            return hit;
        foreach (var merged in root.MergedDictionaries)
        {
            if (FindThemeDictionary(merged, themeKey) is { } nested)
                return nested;
        }
        return null;
    }
}
