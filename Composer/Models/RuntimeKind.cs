namespace Composer.Models;

public enum RuntimeKind
{
    Net11,
    Net10,
    Net9,
}

public static class RuntimeKindExtensions
{
    public static string DisplayName(this RuntimeKind kind) => kind switch
    {
        RuntimeKind.Net11 => ".NET 11",
        RuntimeKind.Net10 => ".NET 10",
        RuntimeKind.Net9  => ".NET 9",
        _                 => kind.ToString(),
    };

    public static string ShortGlyph(this RuntimeKind kind) => kind switch
    {
        RuntimeKind.Net11 => "11",
        RuntimeKind.Net10 => "10",
        RuntimeKind.Net9  => "9",
        _                 => string.Empty,
    };

    // Pre-release runtime — gets a "preview" badge on its chip.
    public static bool IsPreview(this RuntimeKind kind) => kind == RuntimeKind.Net11;

    // Out-of-support / deprecating runtime — gets a dashed border to signal
    // "use at your own risk".
    public static bool IsLegacy(this RuntimeKind kind) => kind == RuntimeKind.Net9;
}
