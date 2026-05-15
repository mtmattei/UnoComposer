using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Composer.Models;

public sealed record UnoDocsReference(
    string DecisionArea,
    string SourceTitle,
    string Url,
    string AppliedConstraint);

public static class UnoDocsGrounding
{
    private static readonly UnoDocsReference BestPractices = new(
        "General implementation",
        "Uno Platform best practices",
        "https://platform.uno/docs/articles/best-practices-uno.html",
        "Prefer Uno-supported patterns and isolate platform-specific exceptions.");

    private static readonly UnoDocsReference Mvux = new(
        "State model",
        "MVUX reactive programming",
        "https://platform.uno/docs/articles/intro.html#mvux",
        "Use MVUX feeds/states as the default state-management posture for generated app briefs.");

    private static readonly UnoDocsReference Navigation = new(
        "Navigation",
        "Uno Extensions Navigation",
        "https://platform.uno/docs/articles/external/uno.chefs/doc/navigation/Overview.html",
        "Use Uno Extensions navigation/regions for route decisions instead of ad-hoc code-behind routing.");

    private static readonly UnoDocsReference DependencyInjection = new(
        "Services",
        "Uno Extensions dependency injection",
        "https://platform.uno/docs/articles/external/uno.extensions/doc/Learn/DependencyInjection/DependencyInjectionOverview.html",
        "Register services with the host and consume them through constructor injection.");

    private static readonly UnoDocsReference Styling = new(
        "Styling and theming",
        "Uno Toolkit lightweight styling",
        "https://platform.uno/docs/articles/external/uno.chefs/doc/toolkit/LightWeightStyling.html",
        "Use reusable resources/theme dictionaries for visual decisions; avoid one-off hardcoded styling.");

    private static readonly UnoDocsReference WinUiStyling = new(
        "XAML resources",
        "WinUI styling and templating guidance for Uno",
        "https://platform.uno/docs/articles/winui-doc-links-development.html#styling-and-templating",
        "Ground XAML styling recommendations in ResourceDictionary, ThemeResource, and control-template patterns.");

    private static readonly UnoDocsReference Tooling = new(
        "Scaffold command",
        "Uno Platform project templates",
        "https://platform.uno/docs/articles/get-started.html",
        "Use `dotnet new unoapp` options that match the selected presentation, theme, platforms, and features.");

    private static readonly UnoDocsReference PlatformFeatures = new(
        "UnoFeatures",
        "Uno SDK single-project features",
        "https://aka.platform.uno/singleproject-features",
        "Keep generated `<UnoFeatures>` and CLI `--features` values aligned.");

    public static ImmutableArray<UnoDocsReference> ForLayer(string layerId)
        => layerId switch
        {
            "ux" => ImmutableArray.Create(BestPractices, Navigation),
            "architecture" => ImmutableArray.Create(BestPractices, Mvux, Navigation, DependencyInjection, PlatformFeatures),
            "design" => ImmutableArray.Create(BestPractices, Styling, WinUiStyling),
            "interactions" => ImmutableArray.Create(BestPractices, Styling),
            "data" => ImmutableArray.Create(BestPractices, Mvux, DependencyInjection),
            "implementation" => ImmutableArray.Create(BestPractices, Mvux, Navigation, DependencyInjection, Styling),
            "scaffold" => ImmutableArray.Create(Tooling, PlatformFeatures),
            _ => ImmutableArray<UnoDocsReference>.Empty,
        };

    public static string BuildPromptBlock(string layerId)
    {
        var refs = ForLayer(layerId);
        if (refs.IsDefaultOrEmpty) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("Uno docs grounding contract:");
        sb.AppendLine("1. Treat the official Uno sources below as constraints for technical implementation decisions.");
        sb.AppendLine("2. When a recommendation is grounded, keep it consistent with the matching source and do not overstate it.");
        sb.AppendLine("3. If a technical recommendation is product-specific or not supported by the sources, label it as an assumption or risk.");
        sb.AppendLine("4. Preserve or add a compact \"Uno docs basis\" section that cites these sources by title and URL.");
        sb.AppendLine();
        sb.AppendLine("Official Uno sources available for this layer:");
        foreach (var r in refs)
            sb.Append("- ").Append(r.DecisionArea).Append(": ").Append(r.SourceTitle).Append(" — ").Append(r.Url).Append(". ").AppendLine(r.AppliedConstraint);
        sb.AppendLine();
        return sb.ToString();
    }

    public static string BuildMarkdownSection(string layerId)
    {
        var refs = ForLayer(layerId);
        if (refs.IsDefaultOrEmpty) return string.Empty;

        var rows = refs
            .Select(r => $"| {r.DecisionArea} | [{r.SourceTitle}]({r.Url}) | {r.AppliedConstraint} |");
        return "## Uno docs basis\n\n" +
               "Technical decisions in this brief are grounded in these official Uno sources. Product-specific choices outside these sources should stay labeled as assumptions or risks.\n\n" +
               "| Decision area | Official source | Applied constraint |\n" +
               "|---|---|---|\n" +
               string.Join("\n", rows) +
               "\n";
    }
}
