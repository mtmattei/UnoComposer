using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Composer.Services;
using Uno.Extensions.Reactive;

namespace Composer.Presentation;

public partial record CompositionModel
{
    private readonly INavigator _navigator;
    private readonly ILayerPreviewService _previewService;
    private readonly IUnoSdkVersionService _sdkVersionService;
    private readonly IBundleExporter _exporter;

    // Per-layer rollback bookkeeping — not feeds; mutated only by command
    // handlers and read inside GeneratePreview / AcceptAndLock / Discard*.
    // Keyed by layer id ("intent", "ux", ...).
    private readonly Dictionary<string, LayerSnapshot> _snapshots = new();
    private readonly Dictionary<string, object> _previewValues = new();
    private readonly Dictionary<string, string> _previewSummary = new();

    // Live Uno.Sdk version chip — single-flight cache so the feed re-reads the
    // same task on every subscription.
    private readonly Lazy<Task<string>> _latestUnoSdkTask;
    private const string UnoSdkFallback = "6.5.29";

    public CompositionModel(
        INavigator navigator,
        ILayerPreviewService previewService,
        IUnoSdkVersionService sdkVersionService,
        IBundleExporter exporter)
    {
        _navigator = navigator;
        _previewService = previewService;
        _sdkVersionService = sdkVersionService;
        _exporter = exporter;
        _latestUnoSdkTask = new Lazy<Task<string>>(LoadLatestUnoSdkAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IFeed<string> LatestUnoSdk => Feed.Async(async ct =>
    {
        return await _latestUnoSdkTask.Value.WaitAsync(ct).ConfigureAwait(false);
    });

    // Active layer prompt — reactive over (ActiveLayer, Intent) through
    // LayerPrompts.For so the lead question + suggestion chips re-derive when
    // the user edits Intent. Replaces the static LayerStatus.LeadingQuestion +
    // hardcoded chip array in the previous ComposerFooter binding.
    public IFeed<LayerPrompt> ActiveLayerPrompt =>
        Feed.Combine(ActiveLayer, Intent).Select(tuple =>
        {
            var (layer, intent) = tuple;
            if (layer is null) return new LayerPrompt(string.Empty, ImmutableArray<string>.Empty);
            var safeIntent = intent ?? Models.Intent.Example;
            var ctx = IntentContext.DeriveFrom(safeIntent);
            return LayerPrompts.For(layer.Id, safeIntent, ctx, layer.State);
        });

    private async Task<string> LoadLatestUnoSdkAsync()
    {
        try
        {
            var live = await _sdkVersionService.GetLatestStableAsync().ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(live) ? UnoSdkFallback : live;
        }
        catch
        {
            return UnoSdkFallback;
        }
    }

    // Initial Intent matches Models.Intent.Example but starts with an empty
    // Platforms string so the chip row begins unselected (the user picks
    // platforms explicitly). TogglePlatform writes the joined chip selection
    // into the Platforms state as the user toggles.
    private static readonly Intent _initialIntent = Models.Intent.Example with { Platforms = "" };

    // ─── Intent: per-field states ────────────────────────────────────────
    // Decomposed from a single IState<Intent> so the MVUX bindable proxy
    // fires per-field PropertyChanged on TwoWay writes (without this, the
    // proxy notifies at the wrapper level only and downstream views miss
    // granular updates). The composed IFeed<Intent> below remains the
    // read-only surface for snapshot/export consumers and the existing
    // ProxyReader.Read<Intent> sites. See docs/SPEC-intent-decomposition.md.
    public IState<string> AppType     => State<string>.Value(this, () => _initialIntent.AppType);
    public IState<string> PrimaryUser => State<string>.Value(this, () => _initialIntent.PrimaryUser);
    public IState<string> Workflow    => State<string>.Value(this, () => _initialIntent.Workflow);
    public IState<string> Platforms   => State<string>.Value(this, () => _initialIntent.Platforms);

    public IFeed<Intent> Intent =>
        Feed.Combine(AppType, PrimaryUser, Workflow, Platforms)
            .Select(t => new Models.Intent(t.Item1, t.Item2, t.Item3, t.Item4));

    // AI-generated 2-4 sentence project overview. Empty until the user clicks
    // "Generate overview" on the intent layer for the first time. Persists
    // across rail navigation; cleared by Reset / ClearExample.
    public IState<string> IntentOverview =>
        State<string>.Value(this, () => string.Empty);

    // ─── DesignTokens: per-field states ──────────────────────────────────
    public IState<string> DesignSurface  => State<string>.Value(this, () => DesignTokens.Default.Surface);
    public IState<string> DesignAction   => State<string>.Value(this, () => DesignTokens.Default.Action);
    public IState<string> DesignInfo     => State<string>.Value(this, () => DesignTokens.Default.Info);
    public IState<string> DesignSuccess  => State<string>.Value(this, () => DesignTokens.Default.Success);
    public IState<string> DesignWarn     => State<string>.Value(this, () => DesignTokens.Default.Warn);
    public IState<string> DesignPanel    => State<string>.Value(this, () => DesignTokens.Default.Panel);
    public IState<string> DesignTag      => State<string>.Value(this, () => DesignTokens.Default.Tag);
    public IState<string> DesignLocked   => State<string>.Value(this, () => DesignTokens.Default.Locked);
    public IState<string> DesignBodyFont => State<string>.Value(this, () => DesignTokens.Default.BodyFont);

    // Nested Feed.Combine — Combine's public arity is below 9, so the nine
    // token states fold into two 4-tuples + a singleton before projecting
    // back to a DesignTokens record.
    public IFeed<DesignTokens> Design =>
        Feed.Combine(
            Feed.Combine(DesignSurface, DesignAction, DesignInfo, DesignSuccess),
            Feed.Combine(DesignWarn, DesignPanel, DesignTag, DesignLocked),
            DesignBodyFont)
        .Select(t => new DesignTokens(
            Surface:  t.Item1.Item1,
            Action:   t.Item1.Item2,
            Info:     t.Item1.Item3,
            Success:  t.Item1.Item4,
            Warn:     t.Item2.Item1,
            Panel:    t.Item2.Item2,
            Tag:      t.Item2.Item3,
            Locked:   t.Item2.Item4,
            BodyFont: t.Item3));

    public IFeed<IImmutableList<DesignSwatch>> DesignSwatches
        => Design.Select(d => (IImmutableList<DesignSwatch>)d.AsOverridePalette());

    // The actual ColorPaletteOverride.xaml source the agent would write —
    // derived from the current DesignTokens, not from a hardcoded snippet.
    // Emits both Light and Dark theme dictionaries so the file matches the
    // Uno.Material expected shape (ResourceDictionary.ThemeDictionaries).
    public IFeed<string> ColorPaletteOverrideXaml
        => Design.Select(d =>
        {
            var sb = new System.Text.StringBuilder();
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
        });

    private static void AppendThemeDictionary(System.Text.StringBuilder sb, string key, ImmutableArray<DesignSwatch> tokens)
    {
        sb.AppendLine($"    <ResourceDictionary x:Key=\"{key}\">");
        foreach (var s in tokens)
            sb.AppendLine($"      <Color x:Key=\"{s.TokenKey}\">{s.Hex}</Color>");
        sb.AppendLine("    </ResourceDictionary>");
    }

    // Bundle tree shown in the Scaffold previewer — derived from the
    // app name (so the root reflects the user's project) and the locked
    // layers so the agent only ships what's been composed.
    private static readonly string[] BundleFiles = new[]
    {
        "intent.md",
        "ux-flow.md",
        "architecture.md",
        "design-system.md",
        "interaction-states.md",
        "data-contracts.md",
        "implementation-plan.md",
        "scaffold-command.md",
        "prompt-context.md",
    };

    public IFeed<string> BundleTree
        => Intent.Select(i => RenderBundleTree(i ?? Models.Intent.Example, activeFile: null));

    // Bundle tree with a "← you are here" marker on the active layer's file.
    // Used by the intent layer's onboarding bundle preview so the user sees
    // their current position in the conveyor of files they're producing.
    public IFeed<string> AnnotatedBundleTree
        => Feed.Combine(Intent, ActiveLayer).Select(t =>
            RenderBundleTree(t.Item1 ?? Models.Intent.Example, t.Item2?.Filename));

    private static string RenderBundleTree(Intent i, string? activeFile)
    {
        var ctx = IntentContext.DeriveFrom(i);
        var sanitized = IntentContext.SanitizeForIdentifier(ctx.AppName);
        var sb = new System.Text.StringBuilder();
        sb.Append(sanitized).AppendLine("-bundle/");
        for (int idx = 0; idx < BundleFiles.Length; idx++)
        {
            var glyph = idx == BundleFiles.Length - 1 ? " └ " : " ├ ";
            sb.Append(glyph).Append(BundleFiles[idx]);
            if (activeFile is not null && BundleFiles[idx] == activeFile)
                sb.Append("    ← you are here");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }


    public IListState<LayerStatus> Layers
        => ListState<LayerStatus>.Value(this, () => (IImmutableList<LayerStatus>)LayerStatus.InitialEight());

    public IState<int> ActiveIndex => State<int>.Value(this, () => 0);

    // Interactions layer — flow tab selection (0=primary, 1=Sign in, 2=Sync data).
    public IState<int> ActiveFlowIndex => State<int>.Value(this, () => 0);

    // Primary flow label is contextualized from intent — "Create job", "Create order", etc.
    public IFeed<string> PrimaryFlowLabel
        => Intent.Select(i =>
        {
            var ctx = IntentContext.DeriveFrom(i ?? Models.Intent.Example);
            var noun = string.IsNullOrWhiteSpace(ctx.EntityNoun) ? "item" : ctx.EntityNoun;
            return $"Create {noun}";
        });

    public IState<string> ComposerPrompt => State<string>.Value(this, () => string.Empty);

    public IState<bool> ExampleVisible => State<bool>.Value(this, () => true);

    // Right-rail Preview/Edit toggle. When false, the previewer is replaced by
    // a TextBox that edits this layer's OverrideMarkdown (Delta §1).
    public IState<bool> IsPreviewMode => State<bool>.Value(this, () => true);

    // Working buffer for Edit mode. Populated from the active layer's
    // OverrideMarkdown (or generated markdown if no override) when entering
    // Edit, and persisted back on leaving Edit.
    public IState<string> EditBuffer => State<string>.Value(this, () => string.Empty);

    // Multi-select platform chips. App loads with NO platforms selected — the
    // user picks explicitly. TogglePlatform keeps Intent.Platforms in sync as
    // a comma-joined string so downstream MarkdownGenerators / IntentContext
    // see the live selection.
    public IState<ImmutableHashSet<PlatformKind>> SelectedPlatforms =>
        State<ImmutableHashSet<PlatformKind>>.Value(this, () => ImmutableHashSet<PlatformKind>.Empty);

    // Single-select runtime chip.
    public IState<RuntimeKind> SelectedRuntime =>
        State<RuntimeKind>.Value(this, () => RuntimeKind.Net10);

    // Reference screenshot file paths attached to the project — passed to
    // LayerPreviewService.GenerateInitialAsync for UX / Design vision seeding.
    public IState<ImmutableArray<string>> ReferenceScreenshotPaths =>
        State<ImmutableArray<string>>.Value(this, () => ImmutableArray<string>.Empty);

    // One acknowledgment line per layer in the Previewing state — captured
    // from the first sentence of the user prompt when GeneratePreview ran.
    public IState<IImmutableDictionary<string, string>> PreviewAcks =>
        State<IImmutableDictionary<string, string>>.Value(this, AllEmptyAcks);

    private static IImmutableDictionary<string, string> AllEmptyAcks() =>
        LayerStatus.InitialEight().ToImmutableDictionary(l => l.Id, _ => string.Empty);

    public IFeed<LayerStatus> ActiveLayer
        => ActiveIndex
            .SelectAsync(async (idx, ct) =>
            {
                var all = await Layers.Option(ct);
                if (!all.IsSome(out var items)) return LayerStatus.InitialEight()[0];
                return items.ElementAtOrDefault(idx) ?? items[0];
            });

    // Coarse revision counter bumped from every mutation entry point that
    // changes an input to the markdown generators but does NOT itself update
    // the Layers IListState (Intent fields, Design tokens, IntentOverview,
    // platform/runtime/screenshot selections). The coverage feed combines
    // this with ActiveLayer/Layers so analysis re-runs whenever any source
    // of the rendered markdown changes.
    public IState<int> CompositionRevision => State<int>.Value(this, () => 0);

    private async ValueTask BumpRevision(CancellationToken ct = default)
        => await CompositionRevision.UpdateAsync(v => v + 1, ct);

    // Per-layer coverage report for the active layer. Re-derives whenever the
    // active layer changes, when the Layers IListState updates (override
    // markdown writes), or when CompositionRevision bumps (Intent/Design/
    // overview/platform/runtime/screenshot edits). Drives the right-rail
    // coverage badge and the footer's missing-section chip row.
    public IFeed<LayerCoverage> ActiveLayerCoverage =>
        Feed.Combine(ActiveLayer, CompositionRevision)
            .SelectAsync(async (_, ct) =>
            {
                var layer = await ActiveLayer;
                if (layer is null) return LayerCoverage.Empty("intent");
                var md = await GetEffectiveMarkdown(layer.Id, ct);
                return LayerCoverageAnalyzer.Analyze(layer.Id, md);
            });

    public IFeed<ImmutableArray<SectionSpec>> MissingSectionsForActive =>
        ActiveLayerCoverage.Select(c => c.Missing);

    public async ValueTask LockAndContinue(CancellationToken ct)
    {
        var idx = await ActiveIndex;
        await Layers.UpdateAsync(items =>
        {
            if (items is null) return LayerStatus.InitialEight();
            var array = items.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                if (i == idx)
                {
                    array[i] = array[i] with { Lifecycle = LayerLifecycle.Locked, State = LayerState.Clean };
                }
                else if (i == idx + 1 && array[i].Lifecycle != LayerLifecycle.Locked)
                {
                    array[i] = array[i] with { Lifecycle = LayerLifecycle.Active };
                }
            }
            return array.ToImmutableArray();
        }, ct);

        if (idx < 7)
            await ActiveIndex.UpdateAsync(_ => idx + 1, ct);

        await ComposerPrompt.UpdateAsync(_ => string.Empty, ct);
    }

    // Rail navigation. The target always becomes Active — including a
    // previously Locked layer — so the user can revise earlier work by
    // clicking the rail. Other locked layers stay locked so their committed
    // state is preserved; non-locked, non-target layers reset to Future.
    public async ValueTask GoToLayer(int targetIndex, CancellationToken ct)
    {
        await Layers.UpdateAsync(items =>
        {
            if (items is null) return LayerStatus.InitialEight();
            var array = items.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                if (i == targetIndex)
                {
                    array[i] = array[i] with { Lifecycle = LayerLifecycle.Active };
                }
                else if (array[i].Lifecycle != LayerLifecycle.Locked)
                {
                    array[i] = array[i] with { Lifecycle = LayerLifecycle.Future };
                }
            }
            return array.ToImmutableArray();
        }, ct);
        await ActiveIndex.UpdateAsync(_ => targetIndex, ct);
    }

    public async ValueTask EnablePreview(CancellationToken ct)
    {
        // Persist any pending edits back to the active layer first.
        await PersistBufferToActiveLayer(ct);
        await IsPreviewMode.UpdateAsync(_ => true, ct);
    }

    public async ValueTask EnableEdit(CancellationToken ct)
    {
        var idx = await ActiveIndex;
        var opt = await Layers.Option(ct);
        var layer = opt.IsSome(out var items) ? items.ElementAtOrDefault(idx) : null;
        var seed = !string.IsNullOrEmpty(layer?.OverrideMarkdown)
            ? layer!.OverrideMarkdown!
            : MarkdownGenerators.For(layer?.Id ?? "intent", await BuildSnapshot(ct));
        await EditBuffer.UpdateAsync(_ => seed, ct);
        await IsPreviewMode.UpdateAsync(_ => false, ct);
    }

    public async ValueTask DismissExample(CancellationToken ct = default)
        => await ExampleVisible.UpdateAsync(_ => false, ct);

    public async ValueTask RegenerateActive(CancellationToken ct)
    {
        var idx = await ActiveIndex;
        await Layers.UpdateAsync(items =>
        {
            if (items is null) return LayerStatus.InitialEight();
            var array = items.ToArray();
            if (idx >= 0 && idx < array.Length)
                array[idx] = array[idx] with { OverrideMarkdown = null };
            return array.ToImmutableArray();
        }, ct);
        await EditBuffer.UpdateAsync(_ => string.Empty, ct);
        await IsPreviewMode.UpdateAsync(_ => true, ct);
    }

    private async ValueTask PersistBufferToActiveLayer(CancellationToken ct)
    {
        var buffer = await EditBuffer ?? string.Empty;
        if (string.IsNullOrWhiteSpace(buffer)) return;
        var idx = await ActiveIndex;
        await Layers.UpdateAsync(items =>
        {
            if (items is null) return LayerStatus.InitialEight();
            var array = items.ToArray();
            if (idx >= 0 && idx < array.Length)
                array[idx] = array[idx] with { OverrideMarkdown = buffer };
            return array.ToImmutableArray();
        }, ct);
    }

    public async ValueTask SetActiveFlow(int index, CancellationToken ct)
        => await ActiveFlowIndex.UpdateAsync(_ => index, ct);

    public async ValueTask UsePrompt(string suggestion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(suggestion)) return;
        await ComposerPrompt.UpdateAsync(_ => suggestion, ct);
    }

    // Section-aware refine command. Wired to the missing-section chips in the
    // composer footer and the coverage badge: clicking a chip writes the
    // section's CoveragePrompt into the composer textarea and flips the
    // active layer to Dirty so the user can hit Generate Preview to fill
    // exactly that gap.
    public async ValueTask RefineSection(string coveragePrompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(coveragePrompt)) return;
        await ComposerPrompt.UpdateAsync(_ => coveragePrompt, ct);
        await MarkActiveDirty(ct);
    }

    public async ValueTask ClearExample(CancellationToken ct)
    {
        await SetIntentAsync(Models.Intent.Empty, ct);
        await IntentOverview.UpdateAsync(_ => string.Empty, ct);
        await SelectedPlatforms.UpdateAsync(_ => ImmutableHashSet<PlatformKind>.Empty, ct);
        await SelectedRuntime.UpdateAsync(_ => RuntimeKind.Net10, ct);
        await ReferenceScreenshotPaths.UpdateAsync(_ => ImmutableArray<string>.Empty, ct);
        await ExampleVisible.UpdateAsync(_ => false, ct);
        await BumpRevision(ct);
    }

    public async ValueTask Reset(CancellationToken ct)
    {
        _snapshots.Clear();
        _previewValues.Clear();
        _previewSummary.Clear();

        await SetIntentAsync(_initialIntent, ct);
        await SetDesignTokensAsync(DesignTokens.Default, ct);
        await IntentOverview.UpdateAsync(_ => string.Empty, ct);
        await Layers.UpdateAsync(_ => LayerStatus.InitialEight(), ct);
        await ActiveIndex.UpdateAsync(_ => 0, ct);
        await ComposerPrompt.UpdateAsync(_ => string.Empty, ct);
        await ExampleVisible.UpdateAsync(_ => true, ct);

        await SelectedPlatforms.UpdateAsync(_ => ImmutableHashSet<PlatformKind>.Empty, ct);
        await SelectedRuntime.UpdateAsync(_ => RuntimeKind.Net10, ct);
        await ReferenceScreenshotPaths.UpdateAsync(_ => ImmutableArray<string>.Empty, ct);
        await PreviewAcks.UpdateAsync(_ => AllEmptyAcks(), ct);
        await BumpRevision(ct);
    }

    // Atomic writes across the per-field states the decomposed Intent /
    // DesignTokens are built from. Fires Task.WhenAll over the field
    // UpdateAsync calls so the composed IFeed re-derives once per batch
    // instead of N times in sequence.
    private async ValueTask SetIntentAsync(Intent v, CancellationToken ct)
    {
        await Task.WhenAll(
            AppType.UpdateAsync(_ => v.AppType, ct).AsTask(),
            PrimaryUser.UpdateAsync(_ => v.PrimaryUser, ct).AsTask(),
            Workflow.UpdateAsync(_ => v.Workflow, ct).AsTask(),
            Platforms.UpdateAsync(_ => v.Platforms, ct).AsTask());
        await BumpRevision(ct);
    }

    private async ValueTask SetDesignTokensAsync(DesignTokens v, CancellationToken ct)
    {
        await Task.WhenAll(
            DesignSurface.UpdateAsync(_ => v.Surface, ct).AsTask(),
            DesignAction.UpdateAsync(_ => v.Action, ct).AsTask(),
            DesignInfo.UpdateAsync(_ => v.Info, ct).AsTask(),
            DesignSuccess.UpdateAsync(_ => v.Success, ct).AsTask(),
            DesignWarn.UpdateAsync(_ => v.Warn, ct).AsTask(),
            DesignPanel.UpdateAsync(_ => v.Panel, ct).AsTask(),
            DesignTag.UpdateAsync(_ => v.Tag, ct).AsTask(),
            DesignLocked.UpdateAsync(_ => v.Locked, ct).AsTask(),
            DesignBodyFont.UpdateAsync(_ => v.BodyFont, ct).AsTask());
        await BumpRevision(ct);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Chip / screenshot commands
    // ────────────────────────────────────────────────────────────────────

    public async ValueTask TogglePlatform(PlatformKind kind, CancellationToken ct = default)
    {
        ImmutableHashSet<PlatformKind>? next = null;
        await SelectedPlatforms.UpdateAsync(set =>
        {
            var current = set ?? ImmutableHashSet<PlatformKind>.Empty;
            next = current.Contains(kind) ? current.Remove(kind) : current.Add(kind);
            return next;
        }, ct);

        if (next is not null)
        {
            var joined = string.Join(", ", next.OrderBy(p => (int)p).Select(p => p.DisplayName()));
            await Platforms.UpdateAsync(_ => joined, ct);
        }

        // Chip toggles are user-driven and only invoked via Button.Command on
        // click — never during initial binding evaluation — so it's safe to
        // unconditionally request the dirty flip. MarkActiveDirty no-ops when
        // the layer is Locked or already Dirty/Previewing.
        await MarkActiveDirty(ct);
        await BumpRevision(ct);
    }

    public async ValueTask SetRuntime(RuntimeKind kind, CancellationToken ct = default)
    {
        await SelectedRuntime.UpdateAsync(_ => kind, ct);
        await MarkActiveDirty(ct);
        await BumpRevision(ct);
    }

    public async ValueTask AddReferenceScreenshot(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        await ReferenceScreenshotPaths.UpdateAsync(paths =>
        {
            var current = paths.IsDefaultOrEmpty ? ImmutableArray<string>.Empty : paths;
            return current.Contains(path) ? current : current.Add(path);
        }, ct);
        await BumpRevision(ct);
    }

    public async ValueTask RemoveReferenceScreenshot(string path, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        await ReferenceScreenshotPaths.UpdateAsync(paths =>
        {
            var current = paths.IsDefaultOrEmpty ? ImmutableArray<string>.Empty : paths;
            return current.Remove(path);
        }, ct);
        await BumpRevision(ct);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Dirty / Preview / Accept / Discard state machine
    // ────────────────────────────────────────────────────────────────────

    public async ValueTask MarkActiveDirty(CancellationToken ct = default)
    {
        var idx = await ActiveIndex;
        // Map index → layer id via the canonical InitialEight ordering rather
        // than reading from the Layers IListState. The IListState may not be
        // materialized yet on the first user interaction (Shell rail binding
        // doesn't necessarily subscribe to its values), in which case
        // Layers.Option returns None and the dirty flip is silently dropped.
        // The id ordering is fixed at type-init, so this is safe.
        var initial = LayerStatus.InitialEight();
        var layerId = initial.ElementAtOrDefault(idx)?.Id;
        if (layerId is null) return;
        await MarkDirty(layerId, ct);
    }

    public async ValueTask MarkDirty(string layerId, CancellationToken ct = default)
    {
        // Atomic check-and-update via UpdateAsync — forces materialization of
        // the IListState on the very first call (Layers.Option returned None
        // before the rail had subscribed, dropping the first chip-click's
        // dirty flip). The layer ref is captured here so CaptureSnapshot can
        // run after the state update without re-reading.
        bool didFlip = false;
        await Layers.UpdateAsync(items =>
        {
            var current = items ?? LayerStatus.InitialEight();
            var array = current.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i].Id != layerId) continue;
                if (array[i].Lifecycle == LayerLifecycle.Locked) return (IImmutableList<LayerStatus>)current;
                if (array[i].State == LayerState.Dirty || array[i].State == LayerState.Previewing) return (IImmutableList<LayerStatus>)current;
                array[i] = array[i] with { State = LayerState.Dirty };
                didFlip = true;
            }
            return array.ToImmutableArray();
        }, ct);

        if (didFlip)
            await CaptureSnapshot(layerId, ct);
    }

    /// <summary>Sets the composer prompt + marks the active layer dirty when
    /// the text becomes non-empty. Used by the textarea binding path.</summary>
    public async ValueTask SetActivePrompt(string text, CancellationToken ct = default)
    {
        await ComposerPrompt.UpdateAsync(_ => text ?? string.Empty, ct);
        if (!string.IsNullOrWhiteSpace(text))
            await MarkActiveDirty(ct);
    }

    /// <summary>Sends the active layer to <see cref="ILayerPreviewService"/>
    /// and stages the proposed values. The proposal is applied to canonical
    /// state immediately so the previewer re-renders WYSIWYG.</summary>
    public async ValueTask GeneratePreview(CancellationToken ct = default)
    {
        var idx = await ActiveIndex;
        var opt = await Layers.Option(ct);
        if (!opt.IsSome(out var items)) return;
        var layer = items.ElementAtOrDefault(idx);
        if (layer is null) return;

        var current = await ReadLayerValues(layer.Id, ct);
        if (current is null) return;

        var prompt = await ComposerPrompt ?? string.Empty;
        var screenshots = await ReferenceScreenshotPaths;
        if (screenshots.IsDefault) screenshots = ImmutableArray<string>.Empty;
        var lockedSummaries = await BuildLockedContextSummaries(ct);

        // Capture snapshot before applying so DiscardPreview can restore.
        await CaptureSnapshot(layer.Id, ct);

        var result = await _previewService.GeneratePreviewAsync(layer.Id, current, prompt, lockedSummaries, ct);
        _previewValues[layer.Id] = result.ProposedValues;
        _previewSummary[layer.Id] = result.Summary;

        await CapturePreviewAck(layer.Id, prompt, ct);

        await ApplyLayerValues(layer.Id, result.ProposedValues, ct);
        await UpdateLayerState(layer.Id, LayerState.Previewing, ct);
    }

    public async ValueTask AcceptAndLock(CancellationToken ct = default)
    {
        var idx = await ActiveIndex;
        var opt = await Layers.Option(ct);
        if (!opt.IsSome(out var items)) return;
        var layer = items.ElementAtOrDefault(idx);
        if (layer is null) return;

        // Non-blocking accept gate — snapshot the missing-section count into
        // LayerStatus.GapCount before locking. The LeftRail item template
        // shows "(N gaps)" inline on locked items; no modal, no banner, just
        // a quiet trace of where the spec was sealed with holes.
        var md = await GetEffectiveMarkdown(layer.Id, ct);
        var coverage = LayerCoverageAnalyzer.Analyze(layer.Id, md);
        var gaps = (!coverage.IsComplete && coverage.Total > 0) ? (int?)coverage.Missing.Length : null;
        await Layers.UpdateAsync(list =>
        {
            if (list is null) return LayerStatus.InitialEight();
            var array = list.ToArray();
            for (int i = 0; i < array.Length; i++)
                if (array[i].Id == layer.Id) array[i] = array[i] with { GapCount = gaps };
            return array.ToImmutableArray();
        }, ct);

        _snapshots.Remove(layer.Id);
        _previewValues.Remove(layer.Id);
        _previewSummary.Remove(layer.Id);

        if (layer.Id == "intent")
            await SeedReferenceDrivenLayers(ct);

        await LockAndContinue(ct);
    }

    private async ValueTask SeedReferenceDrivenLayers(CancellationToken ct)
    {
        var screenshots = await ReferenceScreenshotPaths;
        if (screenshots.IsDefaultOrEmpty) return;

        var intent = await Intent ?? Models.Intent.Example;
        var uxTask = _previewService.GenerateInitialAsync("ux", intent, screenshots, ct);
        var designTask = _previewService.GenerateInitialAsync("design", intent, screenshots, ct);

        await Task.WhenAll(uxTask, designTask);

        if (uxTask.Result is string uxMarkdown && !string.IsNullOrWhiteSpace(uxMarkdown))
            await UpdateLayerOverrideMarkdown("ux", uxMarkdown, ct);

        if (designTask.Result is Models.DesignTokens designTokens)
            await SetDesignTokensAsync(designTokens, ct);
    }

    public async ValueTask DiscardPreview(CancellationToken ct = default)
    {
        var idx = await ActiveIndex;
        var opt = await Layers.Option(ct);
        if (!opt.IsSome(out var items)) return;
        var layer = items.ElementAtOrDefault(idx);
        if (layer is null) return;

        if (_snapshots.TryGetValue(layer.Id, out var snap))
            await RestoreFromSnapshot(layer.Id, snap, ct);

        _snapshots.Remove(layer.Id);
        _previewValues.Remove(layer.Id);
        _previewSummary.Remove(layer.Id);

        await UpdateLayerState(layer.Id, LayerState.Clean, ct);
        await ComposerPrompt.UpdateAsync(_ => string.Empty, ct);
    }

    // DiscardEdits and DiscardPreview have the same effect — GeneratePreview
    // applies proposal immediately, so there's no distinct mid-edit state.
    public async ValueTask DiscardEdits(CancellationToken ct = default)
        => await DiscardPreview(ct);

    /// <summary>Gather all 8 layer markdown files (override > generated) and
    /// hand them to <see cref="IBundleExporter"/> for ZIP export. Bundle name
    /// derives from <c>IntentContext.AppName</c> so it reflects the user's
    /// project. Silent on cancel / file errors — picker may have been
    /// dismissed.</summary>
    public async ValueTask DownloadBundle(CancellationToken ct = default)
    {
        try
        {
            var snap = await BuildSnapshot(ct);
            var ctx = IntentContext.DeriveFrom(snap.Intent);
            var name = IntentContext.SanitizeForIdentifier(ctx.AppName);

            var files = new Dictionary<string, string>();
            foreach (var layer in LayerStatus.Initial)
            {
                files[layer.Filename] = await GetEffectiveMarkdown(layer.Id, ct);
            }

            // Combined prompt-context.md — concatenation of every layer's
            // effective markdown (override > generated) so AI rewrites land
            // in the agent-handoff doc.
            var sb = new System.Text.StringBuilder();
            sb.Append("# prompt-context.md\n\n");
            sb.Append($"Synthesized brief for **{ctx.AppName}**. Eight layers, in order.\n\n");
            sb.Append("---\n\n");
            foreach (var layer in LayerStatus.Initial)
            {
                sb.Append(files[layer.Filename]);
                sb.Append("\n---\n\n");
            }
            files["prompt-context.md"] = sb.ToString();

            await _exporter.SaveBundleAsync(name, files, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bundle export failed: {ex}");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────

    private async Task<object?> ReadLayerValues(string layerId, CancellationToken ct) => layerId switch
    {
        // Bundle Intent + the currently-accepted overview so the AI can refine
        // (rather than rewrite) when the user edits fields and regenerates.
        "intent" => (object?)new IntentPreviewInput(
            await Intent ?? Models.Intent.Example,
            await IntentOverview ?? string.Empty),
        "design" => (object?)await Design,
        _        => await GetEffectiveMarkdown(layerId, ct),
    };

    private async Task<string> GetEffectiveMarkdown(string layerId, CancellationToken ct)
    {
        var opt = await Layers.Option(ct);
        if (!opt.IsSome(out var items)) return string.Empty;
        var layer = items.FirstOrDefault(l => l.Id == layerId);
        if (layer is null) return string.Empty;
        if (!string.IsNullOrEmpty(layer.OverrideMarkdown)) return layer.OverrideMarkdown!;
        var snap = await BuildSnapshot(ct);
        return MarkdownGenerators.For(layerId, snap);
    }

    /// <summary>Frozen capture of all user-controlled inputs — Intent +
    /// Design tokens + platform / runtime selections. Feeds the markdown
    /// generators so the produced files reflect the live composer state,
    /// not just the Intent record.</summary>
    private async Task<ComposerSnapshot> BuildSnapshot(CancellationToken ct)
    {
        var intent = await Intent ?? Models.Intent.Example;
        var design = await Design ?? DesignTokens.Default;
        var platforms = await SelectedPlatforms;
        if (platforms is null || platforms.Count == 0)
            platforms = ImmutableHashSet.Create(PlatformKind.Web, PlatformKind.Android, PlatformKind.iOS);
        var runtime = await SelectedRuntime;
        var overview = await IntentOverview ?? string.Empty;
        var screenshots = await ReferenceScreenshotPaths;
        if (screenshots.IsDefault) screenshots = ImmutableArray<string>.Empty;
        return new ComposerSnapshot(intent, design, platforms, runtime, overview, screenshots);
    }

    private async ValueTask ApplyLayerValues(string layerId, object proposed, CancellationToken ct)
    {
        switch (layerId)
        {
            case "intent" when proposed is string overview:
                await IntentOverview.UpdateAsync(_ => overview, ct);
                await BumpRevision(ct);
                break;
            case "design" when proposed is DesignTokens v:
                await SetDesignTokensAsync(v, ct);
                break;
            default:
                if (proposed is string md)
                    await UpdateLayerOverrideMarkdown(layerId, md, ct);
                break;
        }
    }

    private async ValueTask UpdateLayerOverrideMarkdown(string layerId, string? content, CancellationToken ct)
    {
        await Layers.UpdateAsync(items =>
        {
            if (items is null) return LayerStatus.InitialEight();
            var array = items.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i].Id == layerId)
                    array[i] = array[i] with { OverrideMarkdown = content };
            }
            return array.ToImmutableArray();
        }, ct);
    }

    private async ValueTask UpdateLayerState(string layerId, LayerState state, CancellationToken ct)
    {
        await Layers.UpdateAsync(items =>
        {
            if (items is null) return LayerStatus.InitialEight();
            var array = items.ToArray();
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i].Id == layerId)
                    array[i] = array[i] with { State = state };
            }
            return array.ToImmutableArray();
        }, ct);
    }

    private async ValueTask CaptureSnapshot(string layerId, CancellationToken ct)
    {
        if (_snapshots.ContainsKey(layerId)) return;
        _snapshots[layerId] = layerId switch
        {
            // Field edits aren't reverted by Discard — only the AI-generated
            // overview rolls back. Snapshot stores the prior accepted overview
            // (empty string on first cycle).
            "intent" => new LayerSnapshot(IntentOverview: await IntentOverview ?? string.Empty),
            "design" => new LayerSnapshot(Design: await Design),
            _        => new LayerSnapshot(OverrideMarkdown: await GetLayerOverride(layerId, ct)),
        };
    }

    private async Task<string?> GetLayerOverride(string layerId, CancellationToken ct)
    {
        var opt = await Layers.Option(ct);
        if (!opt.IsSome(out var items)) return null;
        return items.FirstOrDefault(l => l.Id == layerId)?.OverrideMarkdown;
    }

    private async ValueTask RestoreFromSnapshot(string layerId, LayerSnapshot snap, CancellationToken ct)
    {
        switch (layerId)
        {
            case "intent":
                await IntentOverview.UpdateAsync(_ => snap.IntentOverview ?? string.Empty, ct);
                await BumpRevision(ct);
                break;
            case "design" when snap.Design is { } v:
                await SetDesignTokensAsync(v, ct);
                break;
            default:
                await UpdateLayerOverrideMarkdown(layerId, snap.OverrideMarkdown, ct);
                break;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildLockedContextSummaries(CancellationToken ct)
    {
        var dict = new Dictionary<string, string>();
        var opt = await Layers.Option(ct);
        if (!opt.IsSome(out var items)) return dict;

        var intent = await Intent ?? Models.Intent.Example;
        foreach (var layer in items)
        {
            if (layer.Lifecycle != LayerLifecycle.Locked) continue;
            dict[layer.Id] = await BuildLayerSummary(layer.Id, intent, ct);
        }
        return dict;
    }

    private async Task<string> BuildLayerSummary(string layerId, Intent intent, CancellationToken ct)
    {
        switch (layerId)
        {
            case "intent":
                // Prefer the AI-synthesized overview when present — downstream
                // layers get richer context than the raw field concatenation.
                var overview = await IntentOverview ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(overview)) return overview;
                return $"{intent.AppType} for {intent.PrimaryUser}: {intent.Workflow}";
            case "design":
                var d = await Design;
                if (d is null) d = DesignTokens.Default;
                return $"Action {d.Action}, body font {d.BodyFont}.";
            default:
                return SummarizeMarkdown(await GetEffectiveMarkdown(layerId, ct));
        }
    }

    private static string SummarizeMarkdown(string md)
    {
        if (string.IsNullOrWhiteSpace(md)) return "(empty)";
        var flat = md.Replace("\r", " ").Replace("\n", " ");
        return flat.Length <= 200 ? flat : flat.Substring(0, 200).TrimEnd() + "…";
    }

    private async ValueTask CapturePreviewAck(string layerId, string prompt, CancellationToken ct)
    {
        var firstSentence = (prompt ?? string.Empty).Split('.', 2)[0].Trim();
        if (firstSentence.Length > 80) firstSentence = firstSentence.Substring(0, 80).TrimEnd() + "…";
        await PreviewAcks.UpdateAsync(d =>
        {
            var current = d ?? AllEmptyAcks();
            return current.SetItem(layerId, firstSentence);
        }, ct);
    }
}
