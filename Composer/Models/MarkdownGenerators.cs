using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace Composer.Models;

// Per-layer markdown emitters. Each takes a ComposerSnapshot (Intent +
// DesignTokens + Platform/Runtime selections) and produces the markdown the
// agent will write to the corresponding file (intent.md, ux-flow.md, etc.).
//
// Templates branch on Vibe / EntityNoun via BuildScreens, BuildEntityRecord,
// NeedsSchedule etc. so an editorial habit tracker produces "Today / Log /
// Reflect" screens and a streak/log-shaped entity record — not the
// field-service "Dashboard / Schedule / Dispatch" shape with a stray
// `Address` field on a habit.
//
// Specs: composer-delta-brief.md §5; composer-suggestions-port.md grading.
public static class MarkdownGenerators
{
    public static string For(string layerId, ComposerSnapshot snap)
    {
        var ctx = IntentContext.DeriveFrom(snap.Intent);
        var body = layerId switch
        {
            "intent"         => Intent(snap, ctx),
            "ux"             => Ux(snap, ctx),
            "architecture"   => Architecture(snap, ctx),
            "design"         => Design(snap, ctx),
            "interactions"   => Interactions(snap, ctx),
            "data"           => Data(snap, ctx),
            "implementation" => Implementation(snap, ctx),
            "scaffold"       => Scaffold(snap, ctx),
            _                => string.Empty,
        };
        return EnsureRequiredSections(layerId, body);
    }

    // Outer envelope over per-layer generators: appends a placeholder block
    // for any required schema heading that the generator hasn't already
    // emitted. Existing rich content is left untouched. Wildcard sections
    // (UX's first H2) and prefix sections without a meaningful default body
    // are skipped — the generator is expected to cover those directly.
    //
    // The placeholder string is the canonical sentinel
    // <see cref="LayerSectionSchemas.PendingPlaceholder"/>; the coverage
    // analyzer recognizes it as not-real-content so missing sections still
    // surface in the badge + footer chips.
    private static string EnsureRequiredSections(string layerId, string body)
    {
        var schema = LayerSectionSchemas.For(layerId);
        if (schema.RequiredSections.IsDefaultOrEmpty) return body;

        var coverage = LayerCoverageAnalyzer.Analyze(layerId, body);
        if (coverage.Missing.IsDefaultOrEmpty) return body;

        var sb = new StringBuilder(body.TrimEnd());
        foreach (var spec in coverage.Missing)
        {
            // Wildcard headings have no canonical text — generator must emit.
            if (spec.Match == SectionMatch.AnyFirst) continue;

            sb.AppendLine();
            sb.AppendLine();
            sb.Append("## ").AppendLine(spec.Heading);
            sb.AppendLine();
            sb.AppendLine(LayerSectionSchemas.PendingPlaceholder);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Back-compat overload — defaults DesignTokens, platforms, and
    /// runtime. Used by callers that only have an Intent.</summary>
    public static string For(string layerId, Intent intent)
        => For(layerId, ComposerSnapshot.WithDefaults(intent));

    /// <summary>Combined <c>prompt-context.md</c> — every layer's markdown
    /// concatenated with horizontal rules. Used in bundle export when the
    /// caller doesn't have access to per-layer overrides; the CompositionModel
    /// bundle path builds its own override-aware variant directly.</summary>
    public static string BuildPromptContext(ComposerSnapshot snap)
    {
        var ctx = IntentContext.DeriveFrom(snap.Intent);
        var sb = new StringBuilder();
        sb.Append("# prompt-context.md\n\n");
        sb.Append($"Synthesized brief for **{ctx.AppName}**. Eight layers, in order.\n\n");
        sb.Append("---\n\n");
        foreach (var layer in LayerStatus.Initial)
        {
            sb.Append(For(layer.Id, snap));
            sb.Append("\n---\n\n");
        }
        return sb.ToString();
    }

    /// <summary>Shared <c>dotnet new unoapp</c> command emitter. Used by the
    /// Scaffold markdown generator and by the Scaffold layer's "Copy" button.
    /// Branches features on offline-first and platforms on chip selection so
    /// the produced command is exactly what the user would run.</summary>
    public static string BuildScaffoldCommand(ComposerSnapshot snap, IntentContext ctx)
    {
        var features = ctx.IsOfflineFirst
            ? "config,logging,nav,mvux,storage,toolkit,material"
            : "config,http,logging,nav,mvux,storage,toolkit,material";
        var platformsArg = BuildPlatformsArg(snap.Platforms);
        var tfm = snap.Runtime switch
        {
            RuntimeKind.Net11 => "net11.0",
            RuntimeKind.Net9  => "net9.0",
            _                 => "net10.0",
        };
        return $@"dotnet new unoapp \
  -n {ctx.AppName} \
  --tfm {tfm} \
  --platforms {platformsArg} \
  --markup xaml --presentation mvux --theme material \
  --features {features}
";
    }

    public static string BuildUnoComponentRecommendations(IntentContext ctx)
    {
        var entityList = ctx.IsFieldService ? "Jobs / assignments" : $"{ctx.EntityTitle} list";
        var listSurface = ctx.IsFieldService ? "assignment queue" : $"{ctx.EntityNoun} list";
        var detailSurface = ctx.IsFieldService ? "assignment detail" : $"{ctx.EntityNoun} detail";
        var primaryInput = ctx.EntityNoun is "meal"
            ? "Recipe / nutrition editor"
            : ctx.EntityNoun is "workout"
                ? "Workout set editor"
                : $"{ctx.EntityTitle} editor";
        return $@"Prioritize Uno.Toolkit components where a Toolkit implementation exists, then fall back to WinUI primitives with Toolkit/Material styling.

| Need | Recommended Uno component | Package priority | Notes |
|---|---|---|---|
| App structure | `NavigationView`, `TabBar` | WinUI / Uno.Toolkit | Use Uno.Extensions Navigation regions for routing. |
| Dense form layout | `utu:AutoLayout`, `Grid`, `ItemsRepeater` | Uno.Toolkit first | Stable spacing, predictable wrapping, no nested cards. |
| {entityList} | `ItemsRepeater` with reusable row template | WinUI + Uno.Toolkit styling | Prefer virtualization-ready list surfaces for repeated rows. |
| {primaryInput} | `TextBox`, `ComboBox`, `NumberBox`, `DatePicker`, `TimePicker` | WinUI controls | Keep validation inline beside each field. |
| Primary actions | `Button` with Toolkit/Material styles | Uno.Toolkit / Uno.Material | Use icon+text only for clear commands; text for commits. |
| State feedback | `InfoBar`, `ProgressRing`, skeleton rows | WinUI + Uno.Toolkit | Prefer inline loading and non-blocking offline banners. |
| Status and filters | `Chip`, `SegmentedControl` pattern, `ToggleButton` | Uno.Toolkit where available | Use chips for state/category, segmented controls for modes. |
| Reference/media preview | `Image`, `ScrollViewer`, `ItemsRepeater` | WinUI | Use real uploaded screenshots/mockups as downstream context. |

Canvas parity checklist:

- `utu:AutoLayout` - page rhythm, form stacks, and responsive spacing.
- `ItemsRepeater` - virtualized {listSurface} rows and dense scan surfaces.
- Uno.Toolkit/Material `Button` styles - primary save/dispatch actions for {detailSurface}.
- `InfoBar` - offline, sync, validation, and error recovery states.
- `NavigationView` or `TabBar` - route shell, mode switching, and work queues.
- `Chip` / segmented controls - filters, state toggles, and compact selections.";
    }

    // ── Per-layer generators ────────────────────────────────────────────

    private static string Intent(ComposerSnapshot snap, IntentContext ctx)
    {
        var i = snap.Intent;
        var appType = string.IsNullOrWhiteSpace(i.AppType) ? "App" : i.AppType;
        // Project-overview prose lives at the top — the section is always
        // emitted (schema requires it) so the placeholder lands at the proper
        // position before the user generates an overview. Once an overview is
        // accepted the AI prose replaces the placeholder body.
        var overviewContent = string.IsNullOrWhiteSpace(snap.IntentOverview)
            ? LayerSectionSchemas.PendingPlaceholder
            : snap.IntentOverview.Trim();
        var overviewBlock = $@"## Project overview

{overviewContent}

";
        return $@"# {appType}

{appType} for {i.PrimaryUser}.

{overviewBlock}## What this app does

{i.Workflow}

## Platforms

{i.Platforms}

## Canonical terms

Reference these verbatim in downstream code; do not substitute synonyms.

- **Entity:** `{ctx.EntityTitle}` (plural `{ctx.EntityPlural}`)
- **User:** `{ctx.UserSingularTitle}` (plural `{ctx.UserNoun}`)
- **App name:** `{ctx.AppName}`

## Agent prompt

Treat this README as the canonical app description. Downstream layers reference these terms verbatim — preserve the workflow phrasing intact when generating code paths.
";
    }

    private static string Ux(ComposerSnapshot snap, IntentContext ctx)
    {
        var screens = BuildScreens(ctx);
        var sb = new StringBuilder();
        sb.AppendLine("# UX Flows");
        sb.AppendLine();
        sb.AppendLine($"## {ctx.FlowName}");
        sb.AppendLine();
        sb.AppendLine("Five screens in sequence:");
        sb.AppendLine();
        for (int idx = 0; idx < screens.Length; idx++)
            sb.Append(idx + 1).Append(". **").Append(screens[idx].Label).Append("** — ").AppendLine(screens[idx].Hint);
        sb.AppendLine();
        sb.AppendLine("## Why this flow");
        sb.AppendLine();
        var queued = ctx.IsOfflineFirst ? "offline-queued" : "in-flight";
        sb.AppendLine($"Five steps map to a five-second mental model. Confirmation isn't a modal — it's a real terminal screen so the {queued} case has somewhere to live without surprising the user.");
        sb.AppendLine();
        sb.Append(UnoDocsGrounding.BuildMarkdownSection("ux"));
        return sb.ToString();
    }

    private static string Architecture(ComposerSnapshot snap, IntentContext ctx)
    {
        var http = ctx.IsOfflineFirst ? string.Empty : "\n| HTTP (Kiota) | Typed clients, generated | 3 |";
        var httpEdge = ctx.IsOfflineFirst ? string.Empty : "\n| Services → HTTP | calls |";
        var dataLayer = ctx.IsOfflineFirst
            ? $"{ctx.EntityTitle} repository uses MVUX feeds over local Storage, with queued sync and conflict-safe service boundaries."
            : $"{ctx.EntityTitle} repository uses MVUX feeds over DI services, typed HTTP clients, and local cache for responsive reads.";
        var navTopology = $"App shell routes the five-screen {ctx.FlowName} through Uno.Extensions regions: list, create/edit, detail/review, sync/status, confirmation.";
        var solutionLayout = $"{ctx.AppName}/ contains Presentation/Pages, Presentation/ViewModels, Models, Services, Storage, Themes, and App.xaml with RouteMap registration.";
        var unoFeatures = BuildUnoFeatures(ctx);
        var solutionTree = BuildArchitectureSolutionTree(ctx);
        return $@"# Architecture

MVUX + Uno Extensions. {(ctx.IsOfflineFirst ? "Reactive feeds with offline-first sync." : "Reactive feeds with live HTTP.")}

## Data layer

{dataLayer}

| Concern | Decision |
|---|---|
| Source of truth | MVUX `IFeed` / `IState` projections exposed by page models. |
| Entity model | `{ctx.EntityTitle}.cs`, `{ctx.UserSingularTitle}.cs`, and `Schedule.cs` under `Models/`. |
| Service boundary | `{ctx.EntityTitle}Service.cs` owns persistence and remote calls. |
| Offline/cache | {ctx.SyncPostureLabel}; avoid blocking the UI on network round-trips. |

## Navigation topology

{navTopology}

| Route area | Purpose |
|---|---|
| Shell | Owns `NavigationView` / `TabBar` and region host. |
| List route | Scans and filters `{ctx.EntityPlural}`. |
| Create/edit route | Captures the primary `{ctx.EntityNoun}` workflow. |
| Detail/review route | Compares metadata, status, and actions. |
| Confirmation route | Terminal state for success, queued sync, or completion. |

## Solution layout

{solutionLayout}

```text
{solutionTree}
```

## UnoFeatures (csproj)

```xml
<UnoFeatures>{unoFeatures}</UnoFeatures>
```

CLI feature equivalent:

```bash
--features {BuildFeatureArg(ctx)}
```

## Modules

| Module | Role | Files |
|---|---|---|
| Pages | Route surfaces. {ctx.EntityPlural}, {ctx.UserSingularTitle}s | 4 |
| State (MVUX) | Feeds, States, Selection | 6 |
| Navigation | Region-based routes | 2 |
| Services | {BuildServicesDescription(ctx)} | 5 |{http}
| Storage | {ctx.SyncPostureLabel} | 2 |

## Connections

| From → To | Verb |
|---|---|
| Pages → State (MVUX) | binds |
| Pages → Navigation | requests |
| State (MVUX) → Services | consumes |{httpEdge}
| Services → Storage | persists |

{UnoDocsGrounding.BuildMarkdownSection("architecture")}

## Agent prompt

When generating XAML, use `uen:Region.Attached` for navigation surfaces and bind ItemsSource to `{ctx.EntityTitle}sModel.{ctx.EntityPlural}` (an `IFeed`). Never construct ViewModels in code-behind.
";
    }

    private static string BuildUnoFeatures(IntentContext ctx)
    {
        var features = new[] { "Material", "Toolkit", "Mvux", "Navigation", "Storage", "Logging", "Configuration" };
        return ctx.IsOfflineFirst ? string.Join(";", features) : string.Join(";", features.Concat(new[] { "Http" }));
    }

    private static string BuildFeatureArg(IntentContext ctx)
        => ctx.IsOfflineFirst
            ? "config,logging,nav,mvux,storage,toolkit,material"
            : "config,http,logging,nav,mvux,storage,toolkit,material";

    private static string BuildArchitectureSolutionTree(IntentContext ctx)
    {
        var sb = new StringBuilder();
        var screens = ctx.ScreenFlow;
        sb.AppendLine($"{ctx.AppName}/");
        sb.AppendLine($"├── {ctx.AppName}/");
        sb.AppendLine("│   ├── Models/");
        sb.AppendLine($"│   │   ├── {ctx.EntityTitle}.cs");
        sb.AppendLine($"│   │   ├── {ctx.UserSingularTitle}.cs");
        sb.AppendLine("│   │   └── Schedule.cs");
        sb.AppendLine("│   ├── Presentation/");
        sb.AppendLine("│   │   ├── Pages/");
        for (int i = 0; i < screens.Length; i++)
        {
            var glyph = i == screens.Length - 1 ? "│   │   │   └──" : "│   │   │   ├──";
            sb.AppendLine($"{glyph} {ToPageFilename(screens[i].Name)}");
        }
        sb.AppendLine("│   │   ├── ViewModels/");
        sb.AppendLine("│   │   └── RouteMap.cs");
        sb.AppendLine("│   ├── Services/");
        sb.AppendLine($"│   │   └── {ctx.EntityTitle}Service.cs");
        sb.AppendLine("│   ├── Storage/");
        sb.AppendLine($"│   │   └── {ctx.EntityTitle}Store.cs");
        sb.AppendLine("│   ├── Themes/");
        sb.AppendLine("│   │   └── ColorPaletteOverride.xaml");
        sb.Append("│   └── App.xaml");
        return sb.ToString();
    }

    private static string ToPageFilename(string screenName)
    {
        if (string.IsNullOrWhiteSpace(screenName)) return "Page.xaml";
        var parts = screenName.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        return $"{pascal}Page.xaml";
    }

    private static string Design(ComposerSnapshot snap, IntentContext ctx)
    {
        var d = snap.Design;
        var imageryMood = ctx.IsFieldService
            ? "field-context imagery: vehicles, sites, handheld use, maps, and real operational settings"
            : ctx.EntityNoun is "meal"
                ? "ingredient and finished-result photography with natural light and clear food texture"
            : ctx.EntityNoun is "workout"
                ? "active, high-contrast motion imagery with practical training context"
            : ctx.Vibe switch
        {
            Vibe.Clinical    => "clean, documentary, high-trust imagery with real environments and calm lighting",
            Vibe.Financial   => "data-led product imagery, restrained dashboards, and confident but non-glossy screenshots",
            Vibe.Editorial   => "warm editorial imagery with human-scale moments and generous negative space",
            Vibe.Playful     => "bright, expressive illustrations or photography that still keeps controls legible",
            _                => "product-first screenshots and real workflow imagery rather than decorative stock art",
        };
        var iconStyle = ctx.IsFieldService
            ? "solid, high-recognition symbols for route, status, sync, and assignment"
            : ctx.Vibe switch
        {
            Vibe.Playful => "rounded 2px strokes, filled status badges, and simple expressive metaphors",
            _            => "monoline 1.5px strokes with simple filled state indicators",
        };
        var backgroundTreatment = ctx.IsOfflineFirst
            ? $"Warm notepad surface over a muted desk background; offline banners use {d.Warn} sparingly and keep content writable."
            : $"Warm notepad surface over a muted desk background; live panels use {d.Panel} with hairline borders and no heavy chrome.";
        var references = snap.ReferenceScreenshots.IsDefaultOrEmpty
            ? "No reference screenshots are attached. This design system is derived from intent fields and selected tokens."
            : $"Reference-driven: {snap.ReferenceScreenshots.Length} uploaded reference image(s) from the intent stage seed UX flow and design-token generation after the overview is accepted.";
        var sampleHeading = $"{ctx.EntityTitle} #4471 - sample {ctx.EntityNoun}";
        var sampleBody = ctx.IsFieldService
            ? "Arriving in 22 min - awaiting parts confirmation"
            : $"Last {ctx.EntityNoun} captured 22 min ago - ready for review";
        var primaryAction = ctx.IsFieldService ? "Dispatch now" : $"Save {ctx.EntityNoun}";
        var overrideRows = string.Join("\n", d.AsOverridePalette().Select(s => $"| {s.Name} | `{s.TokenKey}` | {s.Hex} | {s.Description} |"));
        var lightRows = string.Join("\n", d.AsLightPaletteTokens().Select(s => $"| {s.Name} | `{s.TokenKey}` | {s.Hex} | {s.Description} |"));
        var darkRows = string.Join("\n", d.AsDarkPaletteTokens().Select(s => $"| {s.Name} | `{s.TokenKey}` | {s.Hex} | {s.Description} |"));
        var paletteXaml = BuildColorPaletteOverrideXaml(d);
        return $@"# Design System

## Reference input

{references}

## 1. Imagery

- Use {imageryMood}.
- Primary imagery should show the actual {ctx.EntityNoun} workflow, not abstract decoration.
- Prefer screenshots, empty states, or generated bitmap illustrations that clarify the task.
- Avoid dark overlays, blurred backgrounds, decorative gradients, and generic stock scenes.

## 2. Iconography

- Style: {iconStyle}.
- Required icons: {ctx.EntityTitle}, {ctx.UserSingularTitle}, schedule/time, sync/offline, success, warning, retry, settings.
- Use icons inside action buttons when the command is tool-like; keep primary text labels for commit actions.
- Selected states use filled or inverted icons; disabled states reduce opacity instead of changing hue.

## 3. Typography

- **Display 26 / SemiBold (Satoshi Variable)** — page headlines
- **Heading 15 / SemiBold** — section titles
- **Body 13 / Inter** — primary copy
- **Caption 10 / Mono / tracked** — eyebrows + metadata
- Use explicit `LineHeight` and `LineStackingStrategy=BlockLineHeight` in XAML styles.
- Use tabular numerals for counts, time slots, IDs, and dashboard metrics.
- Trim single-line file names, entity names, and chip labels with character ellipsis.

Canvas sample:

| Role | Text |
|---|---|
| Heading sample | {sampleHeading} |
| Body sample | {sampleBody} |
| Primary action sample | {primaryAction} |

Component gallery sample:

| Canvas element | Markdown equivalent |
|---|---|
| Primary button | Uno.Toolkit/Material filled `Button` using `SecondaryColor` / `OnSecondaryColor`. |
| TabBar segment | Three-part mode switch with active segment using `Action` and inactive segments using `Panel`. |
| Generated code panel | `ColorPaletteOverride.xaml` below, with Light and Dark theme dictionaries. |

## 4. State matrix

| State | Visual treatment | Copy rule |
|---|---|---|
| Default | Stable layout, no banner | Describe the current {ctx.EntityNoun} task directly. |
| Loading | Skeleton rows in-place | No full-screen spinner unless the whole route is unavailable. |
| Empty | Calm empty panel + one CTA | Say what can be created, not what is missing. |
| Error | Inline message, retry action | Explain recovery; do not show stack traces. |
| Success | Brief confirmation, then settle | Confirm the {ctx.EntityNoun} was saved or queued. |
| Offline | Non-blocking banner | Keep edits local and explain sync state. |

## 5. Layout

- Use full-width page bands with constrained inner content; reserve cards for repeated items and modals.
- Primary flow uses the five-screen sequence from `ux-flow.md` with predictable back/forward navigation.
- Dense operational surfaces should use 8px spacing steps, clear grid columns, and stable row heights.
- Keep action buttons near the form or list they affect; avoid detached command rows.
- On narrow screens, stack metadata below the title before wrapping primary actions.

## 6. Page background

{backgroundTreatment}

- Background must not compete with form fields, state banners, or primary buttons.
- Use one surface hierarchy: page background, panel surface, input surface, selected/action surface.
- Shadows stay subtle and are disabled or simplified during large layout transitions.

## 7. Suggested Uno components

{BuildUnoComponentRecommendations(ctx)}

## 8. Component inventory

| Component | Purpose | Notes |
|---|---|---|
| App shell | Route and page chrome | Navigation via Uno Extensions regions. |
| {ctx.EntityTitle} list row | Scan and compare {ctx.EntityPlural} | Stable height, status chip, primary metadata. |
| {ctx.EntityTitle} detail form | Create/edit workflow | Inline validation and local-save affordance. |
| State banner | Loading/error/offline/success feedback | Non-blocking, one action max. |
| Primary action button | Commit the current step | Uses {d.Action}; no decorative gradients. |
| Secondary text button | Low-risk commands | Link-style, clear text label. |
| Status chip | Lifecycle/state summary | Uses {d.Tag}, {d.Success}, {d.Warn}, and {d.Locked}. |
| Empty state | First-run guidance | One concise sentence and one CTA. |

## 9. Palette posture

{ctx.ActionRationale}

## 10. Tokens (ColorPaletteOverride.xaml)

Override palette shown on the canvas:

| Role | Token | Hex | Notes |
|---|---|---|---|
{overrideRows}

Light theme dictionary:

| Role | Token | Hex | Notes |
|---|---|---|---|
{lightRows}

Dark theme dictionary:

| Role | Token | Hex | Notes |
|---|---|---|---|
{darkRows}

Generated XAML guidance:

- Emit `ColorPaletteOverride.xaml` with both `Light` and `Dark` entries under `ResourceDictionary.ThemeDictionaries`.
- Keep the generated code block synchronized with the token rows above.
- Use the same token names shown in the canvas preview so downstream implementation can copy them directly.

{UnoDocsGrounding.BuildMarkdownSection("design")}

```xml
{paletteXaml}
```
";
    }

    private static string BuildColorPaletteOverrideXaml(DesignTokens d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        sb.AppendLine();
        sb.AppendLine("  <ResourceDictionary.ThemeDictionaries>");
        AppendThemeDictionary(sb, "Light", d.AsLightPaletteTokens());
        sb.AppendLine();
        AppendThemeDictionary(sb, "Dark", d.AsDarkPaletteTokens());
        sb.AppendLine("  </ResourceDictionary.ThemeDictionaries>");
        sb.Append("</ResourceDictionary>");
        return sb.ToString();
    }

    private static void AppendThemeDictionary(StringBuilder sb, string key, ImmutableArray<DesignSwatch> tokens)
    {
        sb.AppendLine($"    <ResourceDictionary x:Key=\"{key}\">");
        foreach (var s in tokens)
            sb.AppendLine($"      <Color x:Key=\"{s.TokenKey}\">{s.Hex}</Color>");
        sb.AppendLine("    </ResourceDictionary>");
    }

    private static string Interactions(ComposerSnapshot snap, IntentContext ctx)
    {
        var primaryFlow = ctx.IsFieldService ? "Create job" : $"Create {ctx.EntityNoun}";
        var queueLine = ctx.IsOfflineFirst
            ? $"Queued locally; banner indicates sync pending. {ctx.UserSingularTitle} shouldn't have to wait for a network round-trip to start their day."
            : $"Show optimistic updates immediately to {ctx.UserNoun}; reconcile on server response.";
        return $@"# Interaction Spec

## Primary flow: {primaryFlow}

Six canonical states per screen — all required by the VSM contract.

| State | Visual | Copy |
|---|---|---|
| Default | Static, no banner | {ctx.DefaultStateLabel} |
| Loading | Skeleton rows; CTA disabled | — |
| Empty | No data; calm CTA | No {ctx.EntityPlural} yet — start one. |
| Error | Inline message + retry | Validation conflict; please retry. |
| Success | Toast; row animates in | New {ctx.EntityNoun} logged. |
| Offline | Banner; queue locally | {queueLine} |

{UnoDocsGrounding.BuildMarkdownSection("interactions")}

## Agent prompt

Every screen must implement all six states. Use `VisualStateManager` groups named exactly: Default, Loading, Empty, Error, Success, Offline.
";
    }

    private static string Data(ComposerSnapshot snap, IntentContext ctx)
    {
        var entityRecord = BuildEntityRecord(ctx);
        var userRecord = BuildUserRecord(ctx);
        var scheduleRecord = NeedsSchedule(ctx) ? "\n\n" + BuildScheduleRecord(ctx) : string.Empty;
        return $@"# Data Contracts

## Entities

```csharp
{entityRecord}

{userRecord}{scheduleRecord}
```

## Why these shapes

Records, not classes — immutable by default. `{ctx.EntityTitle}` is the spine; other records reference it.

{UnoDocsGrounding.BuildMarkdownSection("data")}
";
    }

    private static string Implementation(ComposerSnapshot snap, IntentContext ctx)
    {
        var screens = BuildScreens(ctx);
        var screenList = string.Join(", ", screens.Select(s => s.Label));
        return $@"# Implementation Plan

Six phases. Each phase has a single deliverable, listed dependencies, and an inline agent prompt.

## Phase 1 — Scaffold

Multi-head Uno solution with MVUX + Uno.Extensions. Output: `{ctx.AppName}.sln`, `{ctx.AppName}/*`, Mobile, Desktop, Wasm heads.

> Use `dotnet new unoapp` with the recommended preset, target net10.0, presentation mvux, theme material.

## Phase 2 — Shell

Two regions: top NavBar and main Frame. Routes registered. Output: `Shell.xaml`, `RouteMap.cs`, `NavigationViewModel.cs`.

> All navigation via `uen:Navigation.Request` — never code-behind.

## Phase 3 — Domain

Domain records + MVUX feed and state per entity. Output: `Models/{ctx.EntityTitle}.cs`, `Models/{ctx.UserSingularTitle}.cs`, `Presentation/Models/*Model.cs`.

> One partial record per entity. Mutations through `record with {{ ... }}`.

## Phase 4 — Screens

{screenList}. AutoLayout grids.

> `x:Bind` only. Lightweight styling only.

## Phase 5 — States

All six states wired in `VisualStateManager` across screens.

> Single CommonStates group. Drive transitions via `VisualStateManagerExtensions.States` binding.

## Phase 6 — Polish

Unit tests, MVUX tests, UI tests, contrast and a11y sweep. Output: `{ctx.AppName}.Tests/`, `AutomationProperties`.

> One scenario per layer happy path, plus reset and revisit.

{UnoDocsGrounding.BuildMarkdownSection("implementation")}
";
    }

    private static string Scaffold(ComposerSnapshot snap, IntentContext ctx)
    {
        var cmd = BuildScaffoldCommand(snap, ctx).TrimEnd();
        return $@"# Scaffold

```sh
{cmd}
```

The composition is, for now, complete.

{UnoDocsGrounding.BuildMarkdownSection("scaffold")}
";
    }

    // ── Vibe / EntityNoun branching helpers ─────────────────────────────

    private readonly record struct ScreenDef(string Label, string Hint);

    private static ScreenDef[] BuildScreens(IntentContext ctx)
    {
        if (ctx.IsFieldService) return new[]
        {
            new ScreenDef("Dashboard",    "Today's jobs"),
            new ScreenDef("Job detail",   "Address + parts"),
            new ScreenDef("Schedule",     "Pick time slot"),
            new ScreenDef("Dispatch",     "Notify + go"),
            new ScreenDef("Confirmation", "Synced or queued"),
        };

        if (ctx.Vibe == Vibe.Editorial) return new[]
        {
            new ScreenDef("Today",                      $"Today's {ctx.EntityPlural}"),
            new ScreenDef($"{ctx.EntityTitle} detail",  $"Open {ctx.EntityNoun}"),
            new ScreenDef("Log",                        $"Record {ctx.EntityNoun}"),
            new ScreenDef("Reflect",                    "See patterns"),
            new ScreenDef("Confirmation",               "Saved"),
        };

        if (ctx.Vibe == Vibe.Financial) return new[]
        {
            new ScreenDef($"{ctx.EntityTitle} list",    $"Today's {ctx.EntityPlural}"),
            new ScreenDef($"{ctx.EntityTitle} detail",  $"Open {ctx.EntityNoun}"),
            new ScreenDef("Plan",                       "Set order"),
            new ScreenDef("Execute",                    "Commit"),
            new ScreenDef("Confirmation",               "Done"),
        };

        if (ctx.Vibe == Vibe.Clinical) return new[]
        {
            new ScreenDef($"{ctx.EntityTitle} list",    $"Today's {ctx.EntityPlural}"),
            new ScreenDef($"{ctx.EntityTitle} detail",  "Open record"),
            new ScreenDef("Schedule",                   "Pick visit"),
            new ScreenDef("Note",                       "Record observation"),
            new ScreenDef("Confirmation",               "Saved"),
        };

        // Standard / Utilitarian (non-field) / Playful — keep generic verbs.
        return new[]
        {
            new ScreenDef("Dashboard",                  $"Today's {ctx.EntityPlural}"),
            new ScreenDef($"{ctx.EntityTitle} detail",  $"Open {ctx.EntityNoun}"),
            new ScreenDef("Plan",                       "Choose a slot"),
            new ScreenDef("Commit",                     "Save"),
            new ScreenDef("Confirmation",               "Saved"),
        };
    }

    private static bool NeedsSchedule(IntentContext ctx) =>
        // Habits, notes, workouts, and trades don't need a Schedule entity —
        // they're tracked by date stamp inline (LoggedOn / ExecutedAt) rather
        // than booked into a day's calendar.
        ctx.EntityNoun is not ("habit" or "note" or "workout" or "trade");

    private static string BuildServicesDescription(IntentContext ctx)
    {
        if (ctx.IsFieldService) return "Job, Technician, Schedule";
        if (NeedsSchedule(ctx)) return $"{ctx.EntityTitle}, {ctx.UserSingularTitle}, Schedule";
        return $"{ctx.EntityTitle}, {ctx.UserSingularTitle}";
    }

    private static string BuildEntityRecord(IntentContext ctx)
    {
        var e = ctx.EntityTitle;
        var userT = ctx.UserSingularTitle;

        if (ctx.IsFieldService) return $@"public partial record {e}(
    string Id,
    string Title,
    string Address,
    DateTime? ScheduledAt,
    string? {userT}Id,
    {e}Status Status,
    string? Notes,
    SyncState SyncState);";

        if (ctx.Vibe == Vibe.Editorial)
        {
            if (ctx.EntityNoun == "habit") return $@"public partial record {e}(
    string Id,
    string Title,
    DateOnly LoggedOn,
    int Streak,
    string? Notes,
    SyncState SyncState);";

            // note / journal / recipe / workout
            return $@"public partial record {e}(
    string Id,
    string Title,
    DateTime CreatedAt,
    string Body,
    SyncState SyncState);";
        }

        if (ctx.Vibe == Vibe.Financial) return $@"public partial record {e}(
    string Id,
    string Symbol,
    decimal Quantity,
    decimal Price,
    DateTime ExecutedAt,
    {e}Status Status,
    SyncState SyncState);";

        if (ctx.Vibe == Vibe.Clinical) return $@"public partial record {e}(
    string Id,
    string {userT}Id,
    DateTime VisitedAt,
    string? Notes,
    {e}Status Status,
    SyncState SyncState);";

        // Default — Standard / Utilitarian (non-field) / Playful
        return $@"public partial record {e}(
    string Id,
    string Title,
    DateTime? ScheduledAt,
    string? {userT}Id,
    {e}Status Status,
    string? Notes,
    SyncState SyncState);";
    }

    private static string BuildUserRecord(IntentContext ctx) => $@"public partial record {ctx.UserSingularTitle}(
    string Id,
    string Name,
    string? Phone,
    bool Available);";

    private static string BuildScheduleRecord(IntentContext ctx) => $@"public partial record Schedule(
    DateOnly Day,
    {ctx.EntityTitle}[] {ctx.EntityPlural});";

    private static string BuildPlatformsArg(ImmutableHashSet<PlatformKind> platforms)
    {
        if (platforms is null || platforms.Count == 0)
            return "wasm,ios,android,windows,desktop";
        return string.Join(",", platforms.OrderBy(p => (int)p).Select(p => p switch
        {
            PlatformKind.Web     => "wasm",
            PlatformKind.Windows => "windows",
            PlatformKind.Android => "android",
            PlatformKind.iOS     => "ios",
            PlatformKind.Desktop => "desktop",
            _                    => p.ToString().ToLowerInvariant(),
        }));
    }
}
