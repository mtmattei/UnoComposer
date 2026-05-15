namespace Composer.Models;

public partial record DesignTokens(
    string Surface,
    string Action,
    string Info,
    string Success,
    string Warn,
    string Panel,
    string Tag,
    string Locked,
    string BodyFont)
{
    public static DesignTokens Default { get; } = new(
        Surface: "#FBFAF5",
        Action: "#2F6F68",
        Info: "#3D6F9A",
        Success: "#6F8068",
        Warn: "#B04534",
        Panel: "#F5F5F5",
        Tag: "#7D4FA0",
        Locked: "#FBFAF5",
        BodyFont: "Inter");

    // Material override palette — keys match ColorPaletteOverride.xaml so the
    // swatch list reads as the actual ResourceDictionary the agent writes.
    public ImmutableArray<DesignSwatch> AsOverridePalette() => ImmutableArray.Create(
        new DesignSwatch("Primary",              "#1A1A1A", "PrimaryColor",              "Anchor ink — body, headlines, edges"),
        new DesignSwatch("Secondary",            Action,    "SecondaryColor",            "Action accent — primary CTA"),
        new DesignSwatch("SecondaryContainer",   "#EAF3F1", "SecondaryContainerColor",   "Action soft — banners, hover wash"),
        new DesignSwatch("Tertiary",             Info,      "TertiaryColor",             "Informational — neutral status"),
        new DesignSwatch("Error",                Warn,      "ErrorColor",                "Destructive / failure"),
        new DesignSwatch("Background",           "#EBE4D1", "BackgroundColor",           "Desk backdrop"),
        new DesignSwatch("Surface",              Surface,   "SurfaceColor",              "Notepad surface"),
        new DesignSwatch("SurfaceVariant",       Panel,     "SurfaceVariantColor",       "Sub-panel"),
        new DesignSwatch("Outline",              "#ECECEC", "OutlineColor",              "Hairlines"));

    // Main Material tokens with their On-color pair. Emitted as a Light /
    // Dark pair into ColorPaletteOverride.xaml's ThemeDictionaries so the
    // generated palette file matches Uno.Material's expected shape.
    public ImmutableArray<DesignSwatch> AsLightPaletteTokens() => ImmutableArray.Create(
        new DesignSwatch("Primary",        "#1A1A1A", "PrimaryColor",        "Ink — body and headlines"),
        new DesignSwatch("OnPrimary",      "#FAFAFA", "OnPrimaryColor",      "Content on ink"),
        new DesignSwatch("Secondary",      Action,    "SecondaryColor",      "Action accent"),
        new DesignSwatch("OnSecondary",    "#FFFFFF", "OnSecondaryColor",    "Content on accent"),
        new DesignSwatch("Surface",        Surface,   "SurfaceColor",        "Notepad surface"),
        new DesignSwatch("OnSurface",      "#1A1A1A", "OnSurfaceColor",      "Content on surface"),
        new DesignSwatch("Background",     "#FFFFFF", "BackgroundColor",     "Page backdrop"),
        new DesignSwatch("Error",          Warn,      "ErrorColor",          "Destructive / failure"));

    public ImmutableArray<DesignSwatch> AsDarkPaletteTokens() => ImmutableArray.Create(
        new DesignSwatch("Primary",        "#FAFAFA", "PrimaryColor",        "Ink (inverted) — body and headlines"),
        new DesignSwatch("OnPrimary",      "#0C0D0F", "OnPrimaryColor",      "Content on inverted ink"),
        new DesignSwatch("Secondary",      Action,    "SecondaryColor",      "Action accent (theme-stable)"),
        new DesignSwatch("OnSecondary",    "#0C0D0F", "OnSecondaryColor",    "Content on accent"),
        new DesignSwatch("Surface",        "#16181C", "SurfaceColor",        "Dark surface"),
        new DesignSwatch("OnSurface",      "#FAFAFA", "OnSurfaceColor",      "Content on dark surface"),
        new DesignSwatch("Background",     "#0C0D0F", "BackgroundColor",     "Dark backdrop"),
        new DesignSwatch("Error",          "#E87F6D", "ErrorColor",          "Destructive (dark-tuned)"));
}

public sealed record DesignSwatch(string Name, string Hex, string TokenKey, string Description);
