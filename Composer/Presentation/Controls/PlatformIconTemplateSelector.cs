using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Composer.Presentation.Controls;

public sealed class PlatformIconTemplateSelector : DataTemplateSelector
{
    public DataTemplate? WebTemplate { get; set; }
    public DataTemplate? WindowsTemplate { get; set; }
    public DataTemplate? AndroidTemplate { get; set; }
    public DataTemplate? IOSTemplate { get; set; }
    public DataTemplate? DesktopTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is PlatformKind kind ? PickFor(kind) : null;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => item is PlatformKind kind ? PickFor(kind) : null;

    private DataTemplate? PickFor(PlatformKind kind) => kind switch
    {
        PlatformKind.Web     => WebTemplate,
        PlatformKind.Windows => WindowsTemplate,
        PlatformKind.Android => AndroidTemplate,
        PlatformKind.iOS     => IOSTemplate,
        PlatformKind.Desktop => DesktopTemplate,
        _                    => null,
    };
}
