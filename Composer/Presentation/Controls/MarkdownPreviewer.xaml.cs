using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Composer.Presentation.Controls;

/// <summary>
/// Wrapper around the Uno CommunityToolkit MarkdownTextBlock. Keeping this
/// app-local dependency property lets the rest of Composer keep using the
/// same previewer API while the renderer is supplied by the toolkit.
/// </summary>
public sealed partial class MarkdownPreviewer : UserControl
{
    public static readonly DependencyProperty MarkdownProperty =
        DependencyProperty.Register(
            nameof(Markdown), typeof(string), typeof(MarkdownPreviewer),
            new PropertyMetadata(string.Empty));

    public string? Markdown
    {
        get => (string?)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public MarkdownPreviewer()
    {
        this.InitializeComponent();
    }
}
