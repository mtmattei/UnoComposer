using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Composer.Models;

namespace Composer.Services;

/// <summary>
/// Two AI entry points per layer:
///   1. <see cref="GenerateInitialAsync"/> — kicked off when Intent is locked
///      so each downstream layer's canvas has app-aware initial content rather
///      than the static generated default.
///   2. <see cref="GeneratePreviewAsync"/> — runs when the user types in the
///      composer footer and triggers Generate. Refines the current layer's
///      state using the user's prompt + locked context summaries.
///
/// For typed layers (intent, design), values are records. For every other
/// layer the value is the markdown body (string) — proposal and current alike.
/// </summary>
public interface ILayerPreviewService
{
    /// <summary>Initial seed for a downstream layer from the locked Intent.
    /// Returns null when the layer is not AI-seeded (e.g., intent itself) or
    /// the call fails — caller falls back to the layer's hardcoded default
    /// (typed records) or the markdown generator output (markdown layers).
    /// When <paramref name="screenshotPaths"/> is non-empty, the ux and
    /// design layers route through Anthropic's vision API so Sonnet can see
    /// the reference images.</summary>
    Task<object?> GenerateInitialAsync(
        string layerId,
        Intent intent,
        ImmutableArray<string> screenshotPaths,
        CancellationToken ct = default);

    /// <summary>Refine the current layer state using the user's prompt and the
    /// locked-context summaries from prior layers. Identity-fallback on
    /// missing API key, network error, or parse failure.</summary>
    Task<LayerPreviewResult> GeneratePreviewAsync(
        string layerId,
        object currentValues,
        string userPrompt,
        IReadOnlyDictionary<string, string> lockedContextSummaries,
        CancellationToken ct = default);
}
