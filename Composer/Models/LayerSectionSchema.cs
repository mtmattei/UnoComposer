using System;
using System.Collections.Generic;

namespace Composer.Models;

// How a SectionSpec.Heading is matched against an actual H2 in the rendered
// markdown. Exact = literal equality after numeric-prefix and em-dash
// normalization. Prefix = the H2 starts with the schema heading (used when
// the actual heading carries a dynamic suffix like "Primary flow: <flow>").
// AnyFirst = the first H2 in the document satisfies this row regardless of
// its text (used by UX where the flow heading is dynamic per intent).
public enum SectionMatch { Exact, Prefix, AnyFirst }

// One required heading in a layer's contract. Heading is the canonical text
// (without the "## " prefix). Purpose is shown in tooltips on the coverage
// badge dots. CoveragePrompt is the chip label / composer prompt fragment
// that targets this gap (e.g. "Fill API boundaries").
public sealed record SectionSpec(
    string Heading,
    string Purpose,
    string CoveragePrompt,
    SectionMatch Match = SectionMatch.Exact);

// The set of required H2 headings for one layer. Ordering reflects how the
// generator should emit them and how the AI is told to keep them. Coverage
// only enforces presence + content, not order.
public sealed record LayerSectionSchema(
    string LayerId,
    string Title,
    ImmutableArray<SectionSpec> RequiredSections);

public static class LayerSectionSchemas
{
    // The single canonical placeholder body for "this section exists but has
    // no real content yet". Both the markdown scaffolder (writes it) and the
    // coverage analyzer (recognizes it as not-real-content) reference it,
    // and the AI refinement prompt instructs models to either fill or keep
    // this exact string when they have nothing concrete.
    public const string PendingPlaceholder = "Not applicable / inferred assumption.";

    public static LayerSectionSchema For(string layerId)
        => All.TryGetValue(layerId, out var s) ? s : new LayerSectionSchema(layerId, layerId, ImmutableArray<SectionSpec>.Empty);

    public static IReadOnlyDictionary<string, LayerSectionSchema> All { get; } = Build();

    private static IReadOnlyDictionary<string, LayerSectionSchema> Build()
    {
        var d = new Dictionary<string, LayerSectionSchema>(StringComparer.Ordinal)
        {
            ["intent"] = new("intent", "Intent", ImmutableArray.Create(
                new SectionSpec("Project overview",   "AI-synthesized prose summary of the four intent fields.", "Generate overview"),
                new SectionSpec("What this app does", "The workflow restated in product terms.",                  "Describe the workflow"),
                new SectionSpec("Platforms",          "The selected target platform chips.",                      "Pick platforms"),
                new SectionSpec("Canonical terms",    "Entity / user / app-name pinning so downstream code reuses verbatim.", "Pin canonical terms"),
                new SectionSpec("Agent prompt",       "Single-paragraph downstream-codegen instruction.",         "Add agent guidance"))),

            ["ux"] = new("ux", "UX", ImmutableArray.Create(
                new SectionSpec("Primary flow",  "The 5-screen primary flow heading (text varies per intent).", "Define screens", SectionMatch.AnyFirst),
                new SectionSpec("Why this flow", "Two-sentence rationale for the chosen flow shape.",            "Explain the flow choice"),
                new SectionSpec("Uno docs basis", "Official Uno sources that constrain route and implementation guidance.", "Ground in Uno docs"))),

            ["architecture"] = new("architecture", "Architecture", ImmutableArray.Create(
                new SectionSpec("Data layer",              "Source of truth, entity model, service boundary, offline/cache.", "Fill data layer"),
                new SectionSpec("Navigation topology",     "Routes + region host + per-route purpose.",                       "Add navigation topology"),
                new SectionSpec("Solution layout",         "Folder tree of the produced app.",                                "Add solution layout"),
                new SectionSpec("UnoFeatures (csproj)",    "The <UnoFeatures> element + CLI feature equivalent.",             "Add UnoFeatures"),
                new SectionSpec("Modules",                 "Per-module table: role, files, connections.",                     "List modules"),
                new SectionSpec("Connections",             "Dependency edges between modules.",                               "Map connections"),
                new SectionSpec("API boundaries",          "Service contracts that cross the network.",                       "Fill API boundaries"),
                new SectionSpec("External integrations",   "Third-party services, SDKs, OS APIs the app depends on.",         "Identify integrations"),
                new SectionSpec("Security considerations", "Authn/authz, secret handling, transport, data at rest.",          "Add security considerations"),
                new SectionSpec("Deployment topology",     "Where and how the binary lands; CI/CD posture.",                  "Add deployment topology"),
                new SectionSpec("Risks",                   "Known unknowns + mitigations.",                                   "Identify risks"),
                new SectionSpec("Uno docs basis",           "Official Uno sources that constrain architecture decisions.",      "Ground in Uno docs"),
                new SectionSpec("Agent prompt",            "Single-paragraph downstream-codegen instruction.",                "Add agent guidance"))),

            ["design"] = new("design", "Design System", ImmutableArray.Create(
                new SectionSpec("Reference input",                   "What seeded the design system (uploaded refs, derived from intent, etc.).", "Note reference input"),
                new SectionSpec("Imagery",                           "Mood + subject rules for any imagery used.",         "Define imagery"),
                new SectionSpec("Iconography",                       "Icon style, required icons, selected/disabled rules.", "Define iconography"),
                new SectionSpec("Typography",                        "Type stack + per-role sizes/weights.",               "Define typography"),
                new SectionSpec("State matrix",                      "Per-state visual + copy rules.",                     "Add state matrix"),
                new SectionSpec("Layout",                            "Page bands, spacing system, primary flow shape.",    "Define layout"),
                new SectionSpec("Page background",                   "Surface treatment + chrome rules.",                  "Define page background"),
                new SectionSpec("Suggested Uno components",          "Component-to-need mapping.",                         "List Uno components"),
                new SectionSpec("Component inventory",               "App-specific components + tokens used.",             "Inventory components"),
                new SectionSpec("Palette posture",                   "Token rationale tied to vibe.",                      "Explain palette posture"),
                new SectionSpec("Tokens (ColorPaletteOverride.xaml)", "Token tables + emitted XAML.",                      "Lock tokens"),
                new SectionSpec("Validation patterns",               "Field-level rules + how errors are displayed.",      "Add validation patterns"),
                new SectionSpec("Accessibility rules",               "Contrast, focus, labels, motion.",                   "Add accessibility rules"),
                new SectionSpec("Anti-patterns",                     "Things explicitly not to do in this design system.", "Add anti-patterns"),
                new SectionSpec("Uno docs basis",                    "Official Uno sources that constrain design-system decisions.", "Ground in Uno docs"))),

            ["interactions"] = new("interactions", "Interactions", ImmutableArray.Create(
                new SectionSpec("Primary flow:",            "Six-state matrix for the happy path (heading carries the flow name as suffix).", "Define primary flow", SectionMatch.Prefix),
                new SectionSpec("Interrupted flows",        "Cancel, back, partial-save, lost-network resume.",  "Add interrupted flows"),
                new SectionSpec("Keyboard / touch behavior", "Per-input modality rules.",                        "Add keyboard/touch behavior"),
                new SectionSpec("Acceptance criteria",      "Per-screen testable criteria.",                     "Add acceptance criteria"),
                new SectionSpec("Uno docs basis",           "Official Uno sources that constrain state and styling guidance.", "Ground in Uno docs"),
                new SectionSpec("Agent prompt",             "Single-paragraph downstream-codegen instruction.",  "Add agent guidance"))),

            ["data"] = new("data", "Data", ImmutableArray.Create(
                new SectionSpec("Entities",         "Record definitions for the domain.",            "Define entities"),
                new SectionSpec("Why these shapes", "Rationale for the chosen record shapes.",       "Explain shape choices"),
                new SectionSpec("Persistence",      "Local store, sync posture, migration strategy.", "Define persistence"),
                new SectionSpec("Validation",       "Required fields + invariants.",                  "Add data validation"),
                new SectionSpec("Uno docs basis",   "Official Uno sources that constrain data and service guidance.", "Ground in Uno docs"))),

            ["implementation"] = new("implementation", "Implementation", ImmutableArray.Create(
                new SectionSpec("Phase 1 -- Scaffold", "Output: scaffolded multi-head solution.",      "Detail scaffold phase"),
                new SectionSpec("Phase 2 -- Shell",    "Output: regions + routes registered.",         "Detail shell phase"),
                new SectionSpec("Phase 3 -- Domain",   "Output: domain records + per-entity feeds.",   "Detail domain phase"),
                new SectionSpec("Phase 4 -- Screens",  "Output: AutoLayout pages with x:Bind.",        "Detail screens phase"),
                new SectionSpec("Phase 5 -- States",   "Output: VSM groups across screens.",           "Detail states phase"),
                new SectionSpec("Phase 6 -- Polish",   "Output: tests + a11y + contrast pass.",        "Detail polish phase"),
                new SectionSpec("Uno docs basis",      "Official Uno sources that constrain implementation planning.", "Ground in Uno docs"))),

            ["scaffold"] = new("scaffold", "Scaffold", ImmutableArray<SectionSpec>.Empty),
        };
        return d;
    }
}
