# SPEC — section-schema

Add a per-layer **section schema** that declares which markdown headings each
layer's output is contractually required to emit, then thread that schema
through the markdown generators, the AI refinement prompts, the right-rail
canvas, and the Accept gate. The headline UX win is a coverage indicator
(`N / N sections covered`) on the right-rail header, plus section-aware refine
chips in the composer footer that target specific gaps.

The flow ladder this enables:

> **Intent** defines the product → **UX** defines screens → **Architecture**
> defines system structure → **Design** defines implementation/UI-system rules
> → **Interactions** defines how people operate the app.

Each layer now has a contract. The contract is enforced (gently) at three
points: generator output, AI refinement, and Accept.

---

## 1. Goals & non-goals

**Goals**
- Each layer always emits every required heading, even on first generation.
- AI refinement preserves required headings and fills missing ones — never
  deletes them.
- The user can see, at a glance, how complete the active layer is.
- The user can target a specific gap with a one-click refine chip.
- Accept never blocks, but warns when required sections are empty.

**Non-goals**
- No semantic content quality check — coverage is heading-presence + minimum
  body content, not "is this section *good*."
- No persistence of schema decisions across sessions — schema is a static type.
- No per-user customizable schemas.
- No backwards-compat shims for layers that previously had no schema (we're
  defining schemas for all 8 fresh, no migration).

---

## 2. Files to add / change

### Add
| Path | Purpose |
|---|---|
| `Composer/Models/LayerSectionSchema.cs` | Schema record + the 8 schemas. |
| `Composer/Models/LayerCoverage.cs` | Coverage report record + parser/checker. |
| `Composer/Presentation/Controls/CoverageBadge.xaml{,.cs}` | Right-rail counter + missing-section chip strip. |

### Change
| Path | Reason |
|---|---|
| `Composer/Models/MarkdownGenerators.cs` | Every per-layer generator emits all schema headings; missing content writes the `CoveragePrompt` placeholder. |
| `Composer/Services/LayerPreviewService.cs` | Refinement system prompt enumerates required headings + the "preserve / fill / never delete / mark unknowns" rules. Seed prompts also include the schema. |
| `Composer/Presentation/CompositionModel.cs` | Add `ActiveLayerCoverage` IFeed, `MissingSectionsForActive` IFeed, `RefineSection(string heading)` command. Accept path checks coverage and surfaces the warning state. |
| `Composer/Presentation/CompositionPage.xaml` | Insert `CoverageBadge` between the LIVE FILE header block and the Preview/Edit pill row. |
| `Composer/Presentation/Layers/ComposerFooter.xaml{,.cs}` | Render section-aware refine chips when the active layer has missing required sections. Accept warning InfoBar. |

---

## 3. Models

### 3.1 `LayerSectionSchema`

```csharp
namespace Composer.Models;

public sealed record SectionSpec(
    string Heading,           // exact level-2 heading text without the "## " prefix
    string Purpose,           // one-line description shown in tooltips
    string CoveragePrompt);   // chip label used in the footer ("Fill API boundaries")

public sealed record LayerSectionSchema(
    string LayerId,
    string Title,
    ImmutableArray<SectionSpec> RequiredSections);

public static class LayerSectionSchemas
{
    public static LayerSectionSchema For(string layerId) => All[layerId];
    public static IReadOnlyDictionary<string, LayerSectionSchema> All { get; } = ...
}
```

Schema content (each row → one `SectionSpec`):

**intent** (`# <appType>` is the H1, sections below are H2)
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| Project overview | AI-synthesized prose summary | "Generate overview" |
| What this app does | Workflow restated | "Describe the workflow" |
| Platforms | Selected target chips | "Pick platforms" |
| Canonical terms | Entity / user / app-name pinning | "Pin canonical terms" |
| Agent prompt | Downstream-codegen instruction | "Add agent guidance" |

**ux**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| `<flow name>` | The 5-screen primary flow | "Define screens" |
| Why this flow | Rationale | "Explain the flow choice" |

> The first H2 here is dynamic (`## Today's habits`, `## Job dispatch`, etc.).
> The schema check matches the **first non-empty H2**, not a literal string.
> See §6.

**architecture**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| Data layer | Source of truth, entity model, service boundary, offline/cache | "Fill data layer" |
| Navigation topology | Routes + region host | "Add navigation topology" |
| Solution layout | Folder tree | "Add solution layout" |
| UnoFeatures (csproj) | The `<UnoFeatures>` element + CLI equivalent | "Add UnoFeatures" |
| Modules | Per-module table | "List modules" |
| Connections | Dependency edges | "Map connections" |
| API boundaries | Service contracts crossing the network | "Fill API boundaries" |
| External integrations | Third-party services / SDKs / OS APIs | "Identify integrations" |
| Security considerations | Authn/authz, secrets, transport | "Add security considerations" |
| Deployment topology | How / where the binary lands | "Add deployment topology" |
| Risks | Known unknowns + mitigations | "Identify risks" |
| Agent prompt | Downstream-codegen instruction | "Add agent guidance" |

**design**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| Reference input | What seeded the system | "Note reference input" |
| Imagery | Mood + subject rules | "Define imagery" |
| Iconography | Style + required icons | "Define iconography" |
| Typography | Type stack + roles | "Define typography" |
| State matrix | Per-state visual + copy rules | "Add state matrix" |
| Layout | Page bands + spacing system | "Define layout" |
| Page background | Surface treatment | "Define page background" |
| Suggested Uno components | Component-to-need mapping | "List Uno components" |
| Component inventory | App-specific components + tokens used | "Inventory components" |
| Palette posture | Token rationale | "Explain palette posture" |
| Tokens (ColorPaletteOverride.xaml) | Token tables + emitted XAML | "Lock tokens" |
| Validation patterns | Field-level rules + error display | "Add validation patterns" |
| Accessibility rules | Contrast, focus, labels, motion | "Add accessibility rules" |
| Anti-patterns | What not to do | "Add anti-patterns" |

**interactions**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| Primary flow: `<flow>` | Six-state matrix for the happy path | "Define primary flow" |
| Interrupted flows | Cancel / back / partial-save / lost-network resume | "Add interrupted flows" |
| Keyboard / touch behavior | Per-input modality rules | "Add keyboard/touch behavior" |
| Acceptance criteria | Per-screen testable criteria | "Add acceptance criteria" |
| Agent prompt | Downstream-codegen instruction | "Add agent guidance" |

**data**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| Entities | Record definitions | "Define entities" |
| Why these shapes | Rationale | "Explain shape choices" |
| Persistence | Local store / sync / migrations | "Define persistence" |
| Validation | Required fields / invariants | "Add data validation" |

**implementation**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| Phase 1 — Scaffold | | "Detail scaffold phase" |
| Phase 2 — Shell | | "Detail shell phase" |
| Phase 3 — Domain | | "Detail domain phase" |
| Phase 4 — Screens | | "Detail screens phase" |
| Phase 5 — States | | "Detail states phase" |
| Phase 6 — Polish | | "Detail polish phase" |

**scaffold**
| Heading | Purpose | CoveragePrompt |
|---|---|---|
| (none — scaffold is a single fenced command) | | |

> Scaffold has an **empty `RequiredSections`** array. Coverage always reads
> 1/1 (or N/A — see §5.2). This is intentional: scaffold is the terminus.

### 3.2 `LayerCoverage`

```csharp
namespace Composer.Models;

public sealed record SectionCoverage(
    SectionSpec Section,
    bool Present,            // heading exists in markdown
    bool HasContent);        // has body content beyond the placeholder

public sealed record LayerCoverage(
    string LayerId,
    ImmutableArray<SectionCoverage> Sections)
{
    public int Total => Sections.Length;
    public int Covered => Sections.Count(s => s.Present && s.HasContent);
    public bool IsComplete => Covered == Total;
    public ImmutableArray<SectionSpec> Missing
        => Sections.Where(s => !s.Present || !s.HasContent)
                   .Select(s => s.Section)
                   .ToImmutableArray();
}

public static class LayerCoverageAnalyzer
{
    public static LayerCoverage Analyze(string layerId, string markdown);
}
```

`Analyze` parses the markdown, finds level-2 headings, and matches them
against the schema. Match rules:

- **Exact**: `## Risks` matches the schema `Risks` row.
- **Prefix**: `## Primary flow: Create habit` matches schema
  `Primary flow: <flow>` (matches anything starting with `Primary flow:`).
- **Wildcard first H2**: ux's first schema row uses `*` as Heading; matcher
  treats it as "any level-2 heading appearing first counts."
- **Phase prefix**: `## Phase 1 — Scaffold` matches `Phase 1 — Scaffold`
  ignoring the em-dash variant (— vs --).

**HasContent** = at least one non-blank line *between* this heading and the
next heading at any level, AND the body is not the literal placeholder string
`Not applicable / inferred assumption.` (see §4).

---

## 4. `MarkdownGenerators` changes

Every per-layer generator currently builds markdown imperatively and may or
may not include each schema heading. The change is mechanical:

1. After the existing per-layer body is built, call:
   ```csharp
   return SectionScaffolder.Ensure(layerId, body);
   ```
2. `SectionScaffolder.Ensure` walks the schema for that layer. For any
   required heading **not present**, it appends:
   ```
   ## <Heading>

   Not applicable / inferred assumption.
   ```
   at the end of the body, in schema order.
3. The placeholder string `Not applicable / inferred assumption.` is the
   single canonical sentinel for "I have nothing here yet." Both the AI
   refinement prompt and the coverage analyzer recognize it.
4. **Don't** rewrite the existing generators to use the schema as the
   authoring source. They still hand-write the rich content. The scaffolder
   is purely an outer envelope ensuring no required heading is missing.

This keeps the existing generator output identical when generators already
cover all schema headings, and gracefully fills in placeholders for the new
sections (architecture's API boundaries, security, deployment, risks; design's
validation/accessibility/anti-patterns; interactions' interrupted-flows /
keyboard-touch / acceptance criteria; data's persistence/validation).

---

## 5. `LayerPreviewService` changes

### 5.1 Refinement prompt addendum

In `GeneratePreviewAsync` for non-typed layers (everything except `intent`
and `design`), append to the system prompt:

```
This layer has a required section schema. The required level-2 headings are:

- ## <Heading 1> — <Purpose>
- ## <Heading 2> — <Purpose>
...

Rules:
1. Preserve every required heading exactly. Do not rename them.
2. Fill missing or placeholder ("Not applicable / inferred assumption.")
   sections when you can — derive content from the locked context if needed.
3. Never delete a required heading. If you have nothing concrete, keep the
   heading and write "Not applicable / inferred assumption." or call out the
   missing input as an assumption / risk.
4. You may add additional sections beyond the schema if useful, but they must
   come after all required ones.
```

For the **design** typed layer the schema rules don't apply directly to the
JSON contract (DesignTokens is shape-driven, not heading-driven). Coverage
for design reads the *generated* markdown, not the tokens. No prompt change
needed there.

For **intent**, the existing `GenerateIntentOverviewAsync` writes prose, not
the full markdown — coverage analysis runs against the rendered intent.md and
is informational only (the user can't refine specific intent.md sections from
the footer; that's what the four field inputs + Generate Overview already
covers).

### 5.2 Seed prompt addendum

Same headings list appended to each layer's seed system prompt in
`BuildSeedPrompt`. Same rules. Initial seeds will produce schema-complete
markdown from the first call, not just from refinement.

---

## 6. `CompositionModel` changes

### 6.1 New feeds

```csharp
public IFeed<LayerCoverage> ActiveLayerCoverage =>
    Feed.Combine(ActiveLayer, /* trigger over Layers + design + intent overview + selected platforms / runtime */)
        .SelectAsync(async (_, ct) =>
        {
            var layer = await ActiveLayer;
            if (layer is null) return LayerCoverage.Empty;
            var md = await GetEffectiveMarkdown(layer.Id, ct);
            return LayerCoverageAnalyzer.Analyze(layer.Id, md);
        });

public IFeed<ImmutableArray<SectionSpec>> MissingSectionsForActive =>
    ActiveLayerCoverage.Select(c => c.Missing);

public IFeed<bool> ActiveHasGaps =>
    ActiveLayerCoverage.Select(c => !c.IsComplete);
```

> The combine trigger needs to fire whenever any input that markdown depends
> on changes (Intent overview, DesignTokens, layers' OverrideMarkdown,
> SelectedPlatforms, SelectedRuntime). Cleanest approach: depend on
> `BundleTree`'s upstream — or expose a coarse `CompositionRevision`
> `IState<int>` that's bumped from every mutation entry point. Pick whichever
> is cheaper at implementation time.

### 6.2 New command

```csharp
public async ValueTask RefineSection(string coveragePrompt, CancellationToken ct = default)
{
    if (string.IsNullOrWhiteSpace(coveragePrompt)) return;
    await ComposerPrompt.UpdateAsync(_ => coveragePrompt, ct);
    await MarkActiveDirty(ct);
}
```

Wired to the section-aware refine chips in §7.2. Sets the composer prompt to
the section's `CoveragePrompt` (e.g. `"Fill API boundaries"`) and marks the
layer dirty so the user can hit Generate Preview.

### 6.3 Accept gate

`AcceptAndLock` does not block. It now publishes a one-shot warning when the
covered layer has missing required sections:

```csharp
public IState<string?> AcceptWarning => State<string?>.Value(this, () => null);

public async ValueTask AcceptAndLock(CancellationToken ct = default)
{
    var coverage = await ActiveLayerCoverage;
    if (coverage is { IsComplete: false, Total: > 0 })
    {
        var missing = string.Join(", ", coverage.Missing.Select(s => s.Heading));
        await AcceptWarning.UpdateAsync(_ => $"{LayerLabel(coverage.LayerId)} accepted with gaps: {missing}", ct);
    }
    else
    {
        await AcceptWarning.UpdateAsync(_ => null, ct);
    }
    // ...existing accept path...
}
```

`AcceptWarning` is cleared by the next `GoToLayer` / `RegenerateActive` /
`Reset`. The footer subscribes and renders an InfoBar (warning severity) when
non-null.

---

## 7. UI

### 7.1 `CoverageBadge` — right-rail header

Insert directly above the existing `LIVE FILE` block in `CompositionPage.xaml`
(currently lines ~231–237). Layout:

```
┌─────────────────────────────────────────────────────────┐
│  CoverageBadge                                           │
│  ●●●●●●●●●●●●●○○   12 / 15 sections covered             │
│  [Fill API boundaries] [Add deployment topology] [+1]   │
└─────────────────────────────────────────────────────────┘
LIVE FILE
Updates as the canvas changes...
```

- Filled dot = covered section. Empty dot = missing.
- Counter uses `BodySmallText`.
- Missing sections render as Toolkit-style chips (or recycled `Chip` style
  from existing palette). Click → `RefineSection({CoveragePrompt})`.
- If `> 3` missing, show first 3 + `[+N more]` overflow that scrolls the
  composer footer chip rail (which carries the full set, see §7.2).
- When `IsComplete`, render only the counter — no chips, no dots.
- When `Total == 0` (scaffold), hide the entire badge.

XAML sketch:

```xml
<controls:CoverageBadge
    Coverage="{Binding ActiveLayerCoverage}"
    RefineCommand="{Binding RefineSection}"
    Visibility="{Binding ActiveLayerCoverage, Converter={StaticResource HasSectionsToVisibility}}" />
```

The control is a small UserControl, not a Toolkit component; it's
layer-specific UI not worth abstracting.

### 7.2 Composer footer — section-aware refine chips

Existing footer renders `LayerPrompt.Suggestions` as a chip row. Add a second
chip row underneath, populated from `MissingSectionsForActive`:

- Eyebrow label: `MISSING SECTIONS` (uses `EyebrowTinyText`).
- Chips: one per missing section, label = `CoveragePrompt`. Click invokes
  `RefineSection({CoveragePrompt})` which writes the prompt into the textarea
  and flips the layer to Dirty.
- Hide the entire row + label when `MissingSectionsForActive` is empty.

Visual hierarchy: Suggestions chips stay primary (they're per-layer voice
chips, often opinionated inversions). Missing-section chips are secondary —
slightly muted treatment, mono label. Both rows wrap with the same spacing.

### 7.3 Accept warning InfoBar

In the footer (between the chip rows and the Accept button row), add an
InfoBar bound to `AcceptWarning`:

```xml
<InfoBar Severity="Warning"
         IsOpen="{Binding AcceptWarning, Converter={StaticResource StringToBool}}"
         IsClosable="True"
         Title="Accepted with gaps"
         Message="{Binding AcceptWarning}" />
```

Auto-dismisses on the next layer change (model clears `AcceptWarning`).

---

## 8. Test plan

No formal test project exists in this repo (per `Definition of Done` checks).
Smoke test plan instead:

| # | Scenario | Expected |
|---|---|---|
| 1 | Fresh app → Intent layer | Coverage shows `4 / 5` (overview missing). Missing chip "Generate overview" appears in footer. |
| 2 | Click "Generate overview" chip | Prompt populates textarea; layer flips Dirty; existing GeneratePreview path runs. |
| 3 | Architecture layer, first generation | Coverage shows `7 / 12` (the 5 new sections placeholdered). Right rail badge shows 5 missing chips collapsed to `+2 more`. |
| 4 | Architecture layer, click "Fill API boundaries" → GeneratePreview → wait | Refined markdown contains `## API boundaries` with non-placeholder body. Coverage rolls up to `8 / 12`. |
| 5 | Accept architecture with missing sections | Lock & Continue still fires. Warning InfoBar appears: `Architecture accepted with gaps: External integrations, Security considerations, Deployment topology`. |
| 6 | Navigate forward to Design, then back | InfoBar is dismissed; warning state is cleared. |
| 7 | Scaffold layer | Coverage badge entirely hidden (Total == 0). |
| 8 | Reset | All coverage state recomputes; AcceptWarning clears; everything back to first-time-Intent state. |
| 9 | Edit-mode override that strips `## Risks` | Coverage drops by 1; chip reappears in footer. (Behavior: edit mode user can break their own contract; we surface it, not enforce it.) |

Run on `net10.0-desktop`. Verify by `dotnet build`, then `dotnet run`.

---

## 9. Implementation order (suggested for the fresh session)

1. `LayerSectionSchema.cs` + `LayerCoverage.cs` (pure model; no UI dependencies).
2. `SectionScaffolder.Ensure` + wire into every `MarkdownGenerators` per-layer
   method. Run app, eyeball each layer's markdown — confirm new placeholders
   appear and existing content is unchanged.
3. `LayerCoverageAnalyzer.Analyze` + unit-test by hand against each of the 8
   currently-generated markdowns.
4. `CompositionModel`: feeds, `RefineSection` command, accept gate.
5. `CoverageBadge` UserControl + slot into `CompositionPage.xaml`.
6. Footer chip row + Accept InfoBar.
7. `LayerPreviewService` prompt addenda (seed + refine).
8. Smoke test pass per §8.
9. Conventional commit per checkpoint (one per numbered step).

---

## 10. Unresolved questions

1. **Combine trigger**: should `ActiveLayerCoverage` depend on a coarse
   `CompositionRevision` counter (cheap, slightly over-fires) or on the
   actual upstream feeds (precise, more wiring)? Decide at implementation
   time — start with the counter.
2. **Wildcard first-H2 in ux**: are there ux markdowns where the first H2 is
   *not* the flow? If the user later adds a `## Out of scope` section before
   the flow, the wildcard match silently shifts. Probably fine; flag if
   surfaces as a real issue.
3. **Section reordering**: schema enforces *presence*, not *order*. Should
   the AI be told to keep schema order? Current draft says no — many AI
   outputs get section order subtly different and forcing it costs more
   than it gives. Confirm during implementation.
4. **Chip overflow behavior**: right rail shows first 3 + `+N more`. Does
   `+N more` open a flyout, scroll the footer rail into view, or expand the
   badge inline? Pick during UI step (5).
5. **Design layer edge case**: design's coverage reads the *generated*
   markdown which depends on `DesignTokens` + `IntentContext`. If the user
   never accepts design, is the coverage based on the live preview or the
   last-accepted state? Spec says live preview (matches "what you see"); flag
   if confusing in practice.
6. **i18n**: `CoveragePrompt` strings are inline in the schema, violating
   the project's `Strings/<lang>/` rule. Do we route them through
   `Strings/en/` now, or punt to a follow-up since chip labels are tiny and
   currently English-only? Recommend punt + open an issue.
