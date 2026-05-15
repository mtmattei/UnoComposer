using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Composer.Presentation;

// Fukasawa motion vocabulary, brief §3. Each attached property hooks pointer
// events on the target and tweens a RenderTransform with the "settle" easing.
// Magnitudes stay in the brief's range — 1–3px translates, 1.02× scale.
public static class Motion
{
    private static readonly Duration HoverDuration = new(TimeSpan.FromMilliseconds(200));
    private static readonly Duration PressDuration = new(TimeSpan.FromMilliseconds(80));
    private static readonly Duration NudgeDuration = new(TimeSpan.FromMilliseconds(320));
    private static readonly Duration LeanDuration  = new(TimeSpan.FromMilliseconds(280));

    private static EasingFunctionBase SettleEase()
        => (EasingFunctionBase)Application.Current.Resources["SettleEase"];

    // ─── motion:Lift.Apply — button lift on hover (§3e) ─────────────────
    public static readonly DependencyProperty LiftProperty =
        DependencyProperty.RegisterAttached(
            "Lift", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnLiftChanged));

    public static void SetLift(DependencyObject d, bool value) => d.SetValue(LiftProperty, value);
    public static bool GetLift(DependencyObject d) => (bool)d.GetValue(LiftProperty);

    private static void OnLiftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue) AttachLift(fe);
        else DetachLift(fe);
    }

    private static void AttachLift(FrameworkElement fe)
    {
        EnsureTransform(fe);
        fe.PointerEntered += LiftEnter;
        fe.PointerExited  += LiftExit;
        fe.PointerPressed += LiftPress;
        fe.PointerReleased += LiftEnter;  // returns to hover state if still hovering
    }

    private static void DetachLift(FrameworkElement fe)
    {
        fe.PointerEntered  -= LiftEnter;
        fe.PointerExited   -= LiftExit;
        fe.PointerPressed  -= LiftPress;
        fe.PointerReleased -= LiftEnter;
    }

    private static void LiftEnter(object sender, PointerRoutedEventArgs e)
        => AnimateY((FrameworkElement)sender, -1, HoverDuration);

    private static void LiftExit(object sender, PointerRoutedEventArgs e)
        => AnimateY((FrameworkElement)sender, 0, HoverDuration);

    private static void LiftPress(object sender, PointerRoutedEventArgs e)
        => AnimateY((FrameworkElement)sender, 0, PressDuration);

    // ─── motion:Nudge.Apply — rail stack item slides 2px on hover (§3f) ─
    public static readonly DependencyProperty NudgeProperty =
        DependencyProperty.RegisterAttached(
            "Nudge", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnNudgeChanged));

    public static void SetNudge(DependencyObject d, bool value) => d.SetValue(NudgeProperty, value);
    public static bool GetNudge(DependencyObject d) => (bool)d.GetValue(NudgeProperty);

    private static void OnNudgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            EnsureTransform(fe);
            fe.PointerEntered += NudgeEnter;
            fe.PointerExited  += NudgeExit;
        }
        else
        {
            fe.PointerEntered -= NudgeEnter;
            fe.PointerExited  -= NudgeExit;
        }
    }

    private static void NudgeEnter(object sender, PointerRoutedEventArgs e)
        => AnimateX((FrameworkElement)sender, 2, NudgeDuration);

    private static void NudgeExit(object sender, PointerRoutedEventArgs e)
        => AnimateX((FrameworkElement)sender, 0, NudgeDuration);

    // ─── motion:Lean.Apply — diagram node hover scale to 1.02× (§3i) ────
    public static readonly DependencyProperty LeanProperty =
        DependencyProperty.RegisterAttached(
            "Lean", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnLeanChanged));

    public static void SetLean(DependencyObject d, bool value) => d.SetValue(LeanProperty, value);
    public static bool GetLean(DependencyObject d) => (bool)d.GetValue(LeanProperty);

    private static void OnLeanChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue)
        {
            EnsureScaleTransform(fe);
            fe.PointerEntered += LeanEnter;
            fe.PointerExited  += LeanExit;
        }
        else
        {
            fe.PointerEntered -= LeanEnter;
            fe.PointerExited  -= LeanExit;
        }
    }

    private static void LeanEnter(object sender, PointerRoutedEventArgs e)
        => AnimateScale((FrameworkElement)sender, 1.02, LeanDuration);

    private static void LeanExit(object sender, PointerRoutedEventArgs e)
        => AnimateScale((FrameworkElement)sender, 1.0, LeanDuration);

    // ─── motion:Breathe.Apply — infinite gentle pulse (§3h) ─────────────
    public static readonly DependencyProperty BreatheProperty =
        DependencyProperty.RegisterAttached(
            "Breathe", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnBreatheChanged));

    public static void SetBreathe(DependencyObject d, bool value) => d.SetValue(BreatheProperty, value);
    public static bool GetBreathe(DependencyObject d) => (bool)d.GetValue(BreatheProperty);

    private static void OnBreatheChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if ((bool)e.NewValue) StartBreathe(fe);
    }

    private static void StartBreathe(FrameworkElement fe)
    {
        EnsureScaleTransform(fe);
        var breathEase = (EasingFunctionBase)Application.Current.Resources["BreathEase"];
        var dur = new Duration(TimeSpan.FromMilliseconds(4200));

        var sb = new Storyboard();
        var opacityAnim = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(opacityAnim, fe);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");
        opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),        Value = 1.0,  EasingFunction = breathEase });
        opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2100)), Value = 0.55, EasingFunction = breathEase });
        opacityAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200)), Value = 1.0,  EasingFunction = breathEase });
        opacityAnim.RepeatBehavior = RepeatBehavior.Forever;
        sb.Children.Add(opacityAnim);

        var scaleXAnim = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scaleXAnim, fe);
        Storyboard.SetTargetProperty(scaleXAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),        Value = 1.0,  EasingFunction = breathEase });
        scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2100)), Value = 0.92, EasingFunction = breathEase });
        scaleXAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200)), Value = 1.0,  EasingFunction = breathEase });
        scaleXAnim.RepeatBehavior = RepeatBehavior.Forever;
        sb.Children.Add(scaleXAnim);

        var scaleYAnim = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scaleYAnim, fe);
        Storyboard.SetTargetProperty(scaleYAnim, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero),        Value = 1.0,  EasingFunction = breathEase });
        scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2100)), Value = 0.92, EasingFunction = breathEase });
        scaleYAnim.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(4200)), Value = 1.0,  EasingFunction = breathEase });
        scaleYAnim.RepeatBehavior = RepeatBehavior.Forever;
        sb.Children.Add(scaleYAnim);

        fe.Loaded += (_, _) => sb.Begin();
    }

    // ─── motion:Settle.Apply — fade + 4px translateY on load (§3b/c) ────
    public static readonly DependencyProperty SettleProperty =
        DependencyProperty.RegisterAttached(
            "Settle", typeof(bool), typeof(Motion),
            new PropertyMetadata(false, OnSettleChanged));

    public static void SetSettle(DependencyObject d, bool value) => d.SetValue(SettleProperty, value);
    public static bool GetSettle(DependencyObject d) => (bool)d.GetValue(SettleProperty);

    // Optional delay before settle (recap line uses 280ms).
    public static readonly DependencyProperty SettleDelayMsProperty =
        DependencyProperty.RegisterAttached(
            "SettleDelayMs", typeof(double), typeof(Motion),
            new PropertyMetadata(0.0));

    public static void SetSettleDelayMs(DependencyObject d, double value) => d.SetValue(SettleDelayMsProperty, value);
    public static double GetSettleDelayMs(DependencyObject d) => (double)d.GetValue(SettleDelayMsProperty);

    // Optional duration override.
    public static readonly DependencyProperty SettleDurationMsProperty =
        DependencyProperty.RegisterAttached(
            "SettleDurationMs", typeof(double), typeof(Motion),
            new PropertyMetadata(520.0));

    public static void SetSettleDurationMs(DependencyObject d, double value) => d.SetValue(SettleDurationMsProperty, value);
    public static double GetSettleDurationMs(DependencyObject d) => (double)d.GetValue(SettleDurationMsProperty);

    // Optional starting translateX (in addition to translateY=4 default).
    public static readonly DependencyProperty SettleFromXProperty =
        DependencyProperty.RegisterAttached(
            "SettleFromX", typeof(double), typeof(Motion),
            new PropertyMetadata(0.0));

    public static void SetSettleFromX(DependencyObject d, double value) => d.SetValue(SettleFromXProperty, value);
    public static double GetSettleFromX(DependencyObject d) => (double)d.GetValue(SettleFromXProperty);

    // Optional starting translateY (default 4).
    public static readonly DependencyProperty SettleFromYProperty =
        DependencyProperty.RegisterAttached(
            "SettleFromY", typeof(double), typeof(Motion),
            new PropertyMetadata(4.0));

    public static void SetSettleFromY(DependencyObject d, double value) => d.SetValue(SettleFromYProperty, value);
    public static double GetSettleFromY(DependencyObject d) => (double)d.GetValue(SettleFromYProperty);

    private static void OnSettleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || !(bool)e.NewValue) return;
        fe.Loaded += (_, _) => Settle_Run(fe);
    }

    private static void Settle_Run(FrameworkElement fe, bool fade = true)
    {
        EnsureTransform(fe);
        var fromX = GetSettleFromX(fe);
        var fromY = GetSettleFromY(fe);
        var durationMs = GetSettleDurationMs(fe);
        var delayMs = GetSettleDelayMs(fe);

        if (fade)
            fe.Opacity = 0;
        if (fe.RenderTransform is TranslateTransform t)
        {
            t.X = fromX;
            t.Y = fromY;
        }

        var sb = new Storyboard { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        var ease = SettleEase();
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));

        if (fade)
        {
            var opacity = new DoubleAnimation { From = 0, To = 1, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(opacity, fe);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            sb.Children.Add(opacity);
        }

        if (fromY != 0)
        {
            var y = new DoubleAnimation { From = fromY, To = 0, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(y, fe);
            Storyboard.SetTargetProperty(y, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            sb.Children.Add(y);
        }

        if (fromX != 0)
        {
            var x = new DoubleAnimation { From = fromX, To = 0, Duration = duration, EasingFunction = ease };
            Storyboard.SetTarget(x, fe);
            Storyboard.SetTargetProperty(x, "(UIElement.RenderTransform).(TranslateTransform.X)");
            sb.Children.Add(x);
        }

        sb.Begin();
    }

    // Public: re-fire a settle storyboard on an element (use on layer transitions
    // where Loaded only fires once but we want the animation again).
    public static void RunSettle(FrameworkElement fe, double fromY = 4, double durationMs = 520, double delayMs = 0, bool fade = true)
    {
        SetSettleFromX(fe, 0);
        SetSettleFromY(fe, fromY);
        SetSettleDurationMs(fe, durationMs);
        SetSettleDelayMs(fe, delayMs);
        Settle_Run(fe, fade);
    }

    // ─── helpers ────────────────────────────────────────────────────────
    private static void EnsureTransform(FrameworkElement fe)
    {
        if (fe.RenderTransform is not TranslateTransform)
        {
            fe.RenderTransform = new TranslateTransform();
        }
    }

    private static void EnsureScaleTransform(FrameworkElement fe)
    {
        if (fe.RenderTransform is not ScaleTransform)
        {
            fe.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            fe.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }
    }

    private static void AnimateY(FrameworkElement fe, double to, Duration duration)
    {
        if (fe.RenderTransform is not TranslateTransform tt) return;
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = SettleEase(),
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, fe);
        Storyboard.SetTargetProperty(anim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private static void AnimateX(FrameworkElement fe, double to, Duration duration)
    {
        if (fe.RenderTransform is not TranslateTransform tt) return;
        var anim = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = SettleEase(),
        };
        var sb = new Storyboard();
        Storyboard.SetTarget(anim, fe);
        Storyboard.SetTargetProperty(anim, "(UIElement.RenderTransform).(TranslateTransform.X)");
        sb.Children.Add(anim);
        sb.Begin();
    }

    private static void AnimateScale(FrameworkElement fe, double to, Duration duration)
    {
        if (fe.RenderTransform is not ScaleTransform st) return;
        var sb = new Storyboard();
        var ease = SettleEase();

        var animX = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(animX, fe);
        Storyboard.SetTargetProperty(animX, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        sb.Children.Add(animX);

        var animY = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(animY, fe);
        Storyboard.SetTargetProperty(animY, "(UIElement.RenderTransform).(ScaleTransform.ScaleY)");
        sb.Children.Add(animY);

        sb.Begin();
    }
}
