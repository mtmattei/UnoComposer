namespace Composer.Models;

public enum PlatformKind
{
    Web,
    Windows,
    Android,
    iOS,
    Desktop,
}

public static class PlatformKindExtensions
{
    public static string DisplayName(this PlatformKind kind) => kind switch
    {
        PlatformKind.Web     => "Web",
        PlatformKind.Windows => "Windows",
        PlatformKind.Android => "Android",
        PlatformKind.iOS     => "iOS",
        PlatformKind.Desktop => "Desktop",
        _                    => kind.ToString(),
    };
}
