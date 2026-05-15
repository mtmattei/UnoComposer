namespace Composer.Models;

// Per-layer composer prompt: the question above the input + the 'TRY' chips.
// All copy routes through IntentContext so the voice adapts to the user's intent.
public sealed record LayerPrompt(string Question, ImmutableArray<string> Suggestions);

public static class LayerPrompts
{
    /// <summary>State-aware footer question for the intent layer. Exposed so
    /// the ComposerFooter can re-derive on every PropertyChanged refresh —
    /// the <see cref="For"/> path captures the state at feed-emit time and
    /// can go stale across Clean→Dirty transitions because the underlying
    /// ActiveLayerPrompt feed isn't subscribed to Layers updates.</summary>
    public static string IntentQuestion(LayerState layerState) => layerState switch
    {
        LayerState.Dirty      => "Generate an overview — I'll synthesize from the fields above.",
        LayerState.Previewing => "Review the overview. Accept to continue, or discard to revert.",
        _                     => "Generate an overview when these fields read right.",
    };

    public static LayerPrompt For(string layerId, Intent intent, IntentContext ctx, LayerState layerState = LayerState.Clean) => layerId switch
    {
        "intent"         => Intent(intent, ctx, layerState),
        "ux"             => Ux(ctx),
        "architecture"   => Architecture(ctx),
        "design"         => Design(ctx),
        "interactions"   => Interactions(ctx),
        "data"           => Data(ctx),
        "implementation" => Implementation(ctx),
        "scaffold"       => Scaffold(ctx),
        _                => new LayerPrompt("", ImmutableArray<string>.Empty),
    };

    // The intent layer's footer prompt is now state-aware rather than a live
    // template over field values. The footer's QuestionText doubles as the
    // overview surface — once an overview is generated, ComposerFooter swaps
    // these placeholders for the AI prose itself. Suggestion pills are
    // *inversions* of the current inferences — tapping "Offline-first"
    // means Composer was reading the intent as live-sync and the user wants
    // to flip that, so each pill genuinely changes downstream output (and
    // the next regeneration).
    private static LayerPrompt Intent(Intent intent, IntentContext ctx, LayerState layerState) =>
        new(IntentQuestion(layerState), ImmutableArray.Create(
            ctx.IsOfflineFirst ? "Live sync only" : "Offline-first",
            ctx.IsMobileFirst  ? "Add desktop"    : "Mobile-first",
            ctx.IsMulti        ? "Single role"    : $"Multi-{ctx.UserNoun}"));

    private static LayerPrompt Ux(IntentContext ctx)
    {
        var subj = ctx.IsFieldService ? "schedule" : $"{ctx.EntityNoun}";
        var primary = ctx.IsFieldService
            ? "Drag-to-reorder schedule, or list-with-time-pickers? Stay on screen after dispatch, or return to dashboard?"
            : $"For the {ctx.EntityNoun} flow — should {ctx.UserNoun} land back on the dashboard after each action, or stay in context?";
        return new LayerPrompt(primary, ImmutableArray.Create(
            $"Drag-to-reorder {subj}",
            "List with time pickers",
            ctx.IsOfflineFirst ? "Stay on screen" : "Return to dashboard"));
    }

    private static LayerPrompt Architecture(IntentContext ctx)
    {
        var actor = ctx.IsMulti ? $"{ctx.UserNoun}" : "the user";
        var subj = ctx.EntityNoun;
        var question = ctx.IsOfflineFirst
            ? $"Offline-first means the queue is load-bearing. Does {subj} sync run in the background, or only when {actor} pulls?"
            : $"Does {subj} logic live in a service, or stay inside State (MVUX)? Do {actor} authenticate, or is access role-less?";
        return new LayerPrompt(question, ImmutableArray.Create(
            ctx.IsMulti ? $"Multiple {ctx.UserNoun} roles" : $"Single {ctx.UserSingular} role",
            ctx.IsOfflineFirst ? "Queue + retry" : "Live sync",
            "No auth"));
    }

    private static LayerPrompt Design(IntentContext ctx) => new(
        ctx.Vibe switch
        {
            Vibe.Clinical    => "Cool, restrained tones for a clinical setting. Keep the teal accent, or swap to a calmer indicator hue?",
            Vibe.Financial   => "Conservative palette. Reserve saturated hues for gain/loss only — stay with teal for the action, or pick something more brand-neutral?",
            Vibe.Editorial   => "Warm paper tones suit editorial use. Stay with teal for the action, or pick something more bookish?",
            Vibe.Playful     => "A warm palette with one accent that earns attention. Stay with teal, or pick a softer hue?",
            Vibe.Utilitarian => "The palette is low-chroma for outdoor visibility. Want a brand override on Action, or stay with teal?",
            _                => "A quiet palette with one accent that earns attention. Stay with teal, or pick something brand-aligned?",
        },
        ImmutableArray.Create("Brand override", "Stay with teal", "Try a cooler accent"));

    private static LayerPrompt Interactions(IntentContext ctx)
    {
        var actor = ctx.UserSingular;
        var question = ctx.IsOfflineFirst
            ? $"Offline state — queue silently, or always show a sync-pending banner for {ctx.UserNoun}?"
            : $"Show optimistic updates immediately to {actor}s, or wait for server confirmation before reflecting the change?";
        return new LayerPrompt(question, ImmutableArray.Create(
            "Queue silently",
            "Banner on offline",
            "Optimistic everywhere"));
    }

    private static LayerPrompt Data(IntentContext ctx) => new(
        $"Three entities — all nullable, or do {ctx.EntityTitle}/{Title(ctx.UserSingular)} have explicit non-null required keys?",
        ImmutableArray.Create("All nullable", "Required keys", "Strict typing"));

    private static LayerPrompt Implementation(IntentContext ctx) => new(
        "Six phases, plain dependencies, agent prompts inline. Anything else to wire in before sealing the plan?",
        ImmutableArray.Create("Add tests phase", "Drop polish phase", "Reorder phases"));

    private static LayerPrompt Scaffold(IntentContext ctx) => new(
        "", ImmutableArray<string>.Empty);

    private static string Title(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
