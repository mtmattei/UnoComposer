namespace Composer.Models;

/// <summary>
/// Frozen capture of the composer's user-controlled inputs at one moment in
/// time. Pure data; no MVUX feed plumbing — read by <see cref="MarkdownGenerators"/>
/// to produce stack- and domain-correct markdown without depending on AI
/// augmentation.
///
/// <see cref="Intent"/> is the source of the noun-substitution chain; the rest
/// shape the templates: <see cref="Design"/> drives the design-system tokens
/// rendered in markdown, <see cref="Platforms"/> + <see cref="Runtime"/> shape
/// the final <c>dotnet new unoapp</c> scaffold command.
/// </summary>
public record ComposerSnapshot(
    Intent Intent,
    DesignTokens Design,
    ImmutableHashSet<PlatformKind> Platforms,
    RuntimeKind Runtime,
    string IntentOverview = "",
    ImmutableArray<string> ReferenceScreenshots = default)
{
    /// <summary>Snapshot with stack defaults — used by the back-compat
    /// <see cref="MarkdownGenerators.For(string, Intent)"/> overload so callers
    /// that only have an Intent (e.g., the static seed code in tests) still
    /// produce reasonable output.</summary>
    public static ComposerSnapshot WithDefaults(Intent intent) => new(
        intent ?? Intent.Example,
        DesignTokens.Default,
        ImmutableHashSet.Create(PlatformKind.Web, PlatformKind.Android, PlatformKind.iOS),
        RuntimeKind.Net10,
        IntentOverview: "",
        ReferenceScreenshots: ImmutableArray<string>.Empty);
}
