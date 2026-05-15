namespace Composer.Models;

public enum LayerLifecycle { Future, Active, Locked }
public enum LayerState { Clean, Dirty, Previewing }

public partial record LayerStatus(
    string Id,
    int Index,
    string Label,
    string Filename,
    string LeadingQuestion,
    LayerLifecycle Lifecycle,
    LayerState State,
    string? Summary,
    string? OverrideMarkdown,
    // Snapshot of how many required schema sections were missing at the
    // moment this layer was accepted. Set by CompositionModel.AcceptAndLock
    // when Lifecycle flips to Locked; null on unlocked layers. Surfaced in
    // the LeftRail composition stack as "(N gaps)" so the user keeps a
    // calm, non-blocking reminder that an earlier layer was sealed with
    // missing sections.
    int? GapCount = null)
{
    public bool IsLocked => Lifecycle == LayerLifecycle.Locked;
    public bool IsActive => Lifecycle == LayerLifecycle.Active;
    public bool IsFuture => Lifecycle == LayerLifecycle.Future;

    // Canonical initial-state set, computed once at type-init. LayerStatus is
    // a record (value-equal, immutable) so sharing the same instance across
    // every callsite is safe — record `with` expressions produce new instances
    // when state changes, leaving Initial untouched. Replaces a per-call
    // allocation of 8 records + ImmutableArray (CompositionModel calls
    // InitialEight() ~11x during a session of state updates, plus once on
    // first ListState materialisation).
    public static ImmutableArray<LayerStatus> Initial { get; } = ImmutableArray.Create(
        new LayerStatus("intent",         0, "Intent",         "intent.md",         "If I summarize the intent right now, the agent has enough to scaffold a meaningful skeleton. Anything else worth adding before locking?", LayerLifecycle.Active, LayerState.Clean, null, null),
        new LayerStatus("ux",             1, "UX",             "ux-flow.md",        "Drag-to-reorder schedule, or list-with-time-pickers? Stay on screen after dispatch, or return to dashboard?", LayerLifecycle.Future, LayerState.Clean, null, null),
        new LayerStatus("architecture",   2, "Architecture",   "architecture.md",   "Does job logic live in a service, or stay inside State (MVUX)? Do users authenticate, or is access role-less?", LayerLifecycle.Future, LayerState.Clean, null, null),
        new LayerStatus("design",         3, "Design System",  "design-system.md",  "Stay with teal for the next-action accent, or pick something more brand-aligned?", LayerLifecycle.Future, LayerState.Clean, null, null),
        new LayerStatus("interactions",   4, "Interactions",   "interaction-states.md", "Offline-first — queue silently, or always show a sync-pending banner?", LayerLifecycle.Future, LayerState.Clean, null, null),
        new LayerStatus("data",           5, "Data",           "data-contracts.md", "Three entities — all nullable, or do Job/Technician have explicit non-null required keys?", LayerLifecycle.Future, LayerState.Clean, null, null),
        new LayerStatus("implementation", 6, "Implementation", "implementation-plan.md", "Six phases, plain dependencies, agent prompts inline. Anything else to wire in before sealing the plan?", LayerLifecycle.Future, LayerState.Clean, null, null),
        new LayerStatus("scaffold",       7, "Scaffold",       "scaffold-command.md", "The composition is, for now, complete.", LayerLifecycle.Future, LayerState.Clean, null, null));

    /// <summary>Backwards-compatible accessor — returns the cached <see cref="Initial"/>.</summary>
    public static ImmutableArray<LayerStatus> InitialEight() => Initial;
}
