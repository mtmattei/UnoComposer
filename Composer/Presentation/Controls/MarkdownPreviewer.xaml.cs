using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Composer.Presentation.Controls;

/// <summary>
/// Lightweight markdown preview for the right rail. It intentionally uses core
/// WinUI elements instead of MarkdownTextBlock so desktop rendering stays
/// deterministic across Uno targets.
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

        MarkdownStack.Children.Clear();

        var markdown = Markdown ?? string.Empty;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();
        var code = new List<string>();
        var inCode = false;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(paragraph);
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
                AddText("• " + CleanInline(line[2..]), "BodySmallText", "Ink2Brush");
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph(paragraph);
        if (code.Count > 0)
            AddCodeBlock(string.Join(Environment.NewLine, code));

        if (MarkdownStack.Children.Count == 0)
            AddText("No markdown generated yet.", "BodySmallText", "Ink3Brush");
    }

    private void FlushParagraph(List<string> paragraph)
    {
        if (paragraph.Count == 0) return;
        AddText(CleanInline(string.Join(" ", paragraph)), "BodySmallText", "Ink2Brush");
        paragraph.Clear();
    }

    private void AddHeading(string line)
    {
        var level = line.StartsWith("### ", StringComparison.Ordinal) ? 3
            : line.StartsWith("## ", StringComparison.Ordinal) ? 2
            : 1;
        var text = CleanInline(line.Substring(level + 1));
        AddText(text, level == 1 ? "HeadlineSmallText" : "LabelLargeText", "InkBrush", topMargin: level == 1 ? 0 : 8);
    }

    private void AddCodeBlock(string text)
    {
        var block = CreateTextBlock(text.TrimEnd(), "MonoBodyText", "CodeDarkForegroundBrush");
        block.TextWrapping = TextWrapping.Wrap;

        MarkdownStack.Children.Add(new Border
        {
            Background = Brush("CodeDarkSurfaceBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Child = block
        });
    }

    private void AddText(string text, string styleKey, string brushKey, double topMargin = 0)
    {
        var block = CreateTextBlock(text, styleKey, brushKey);
        if (topMargin > 0)
            block.Margin = new Thickness(0, topMargin, 0, 0);
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

    private static string CleanInline(string text)
        => text.Replace("**", string.Empty, StringComparison.Ordinal)
               .Replace("__", string.Empty, StringComparison.Ordinal)
               .Replace("`", string.Empty, StringComparison.Ordinal);
}
