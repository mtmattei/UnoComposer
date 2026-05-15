namespace Composer.Models;

/// <summary>
/// Per-layer rollback snapshot. Captured when a layer transitions Clean → Dirty
/// so DiscardEdits / DiscardPreview can restore the pre-edit values. Only the
/// relevant slice is populated for any one layer's snapshot — the rest are null.
///
/// Intent layer snapshots its <see cref="IntentOverview"/> prose (the AI-
/// synthesized project overview); field edits to the four Intent IStates are
/// not reverted by Discard. Design is typed canonical state; every other
/// layer's canonical state lives as a string in LayerStatus.OverrideMarkdown
/// (or the generated markdown when no override is set), so only one of the
/// three slots is ever populated per snapshot.
/// </summary>
public record LayerSnapshot(
    DesignTokens? Design           = null,
    string?       OverrideMarkdown = null,
    string?       IntentOverview   = null);
