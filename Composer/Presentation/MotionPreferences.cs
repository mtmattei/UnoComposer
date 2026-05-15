namespace Composer.Presentation;

// Global toggle for animation playback on chip-style controls. Passed as the
// useTransitions flag to VisualStateManager.GoToState — flip to false to honor
// a reduced-motion preference without rewriting templates.
public static class MotionPreferences
{
    public static bool AnimationsEnabled { get; set; } = true;
}
