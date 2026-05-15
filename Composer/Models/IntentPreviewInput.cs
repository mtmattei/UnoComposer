namespace Composer.Models;

/// <summary>
/// Input shape for the intent layer's <c>GeneratePreviewAsync</c> call. Bundles
/// the four field values (Intent record) with the currently-accepted overview
/// prose so the AI can refine the existing overview rather than rewrite from
/// scratch on every cycle. Identity-fallback (no API key, network error)
/// returns <see cref="CurrentOverview"/> unchanged so the layer's state machine
/// proceeds as if the AI returned the prior value.
/// </summary>
public sealed record IntentPreviewInput(Intent Intent, string CurrentOverview);
