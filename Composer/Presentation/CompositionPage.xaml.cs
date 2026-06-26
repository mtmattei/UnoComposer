using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation;

public sealed partial class CompositionPage : Page
{
    private const double IntentMaxWidth     = 680;
    private const double ExpandedMaxWidth   = 1500;
    private const double LeftRailOpenWidth  = 244;
    private const double RightRailOpenWidth = 320;

    private const int ExpandDurationMs = 520;
    private const int RailDurationMs   = 320;
    private const int RailStaggerMs    = 200;

    private bool _expanded = false;

    private INotifyPropertyChanged? _bindable;
    private PropertyInfo? _activeIndexProperty;
    private DispatcherTimer? _pollTimer;
    private int _lastSeenIndex = -1;
    private int _previousSeenIndex = -1;
    private bool _suppressEditTextChanged;
    private bool? _lastSeenPreviewMode;
    private string? _lastSeenOverrideMarkdown;
    private string? _lastSeenGeneratedMarkdownSignature;
    private int _lastSeenRevision = int.MinValue;
    private string? _generatedMarkdownLayerId;
    private string? _generatedMarkdownSignature;
    private string? _generatedMarkdown;
    private EventHandler<object>? _scrollClampHandler;
    private readonly Dictionary<TranslateTransform, EventHandler<object>> _railHoverHandlers = new();
    private const double ThemePullRestY = 16;
    private const double ThemePullMax = 64;
    private const double ThemePullThreshold = 54;
    private const double ThemePullTapTarget = 62;
    private bool _isThemePullDragging;
    private double _themePullStartPointerY;
    private double _themePullPointerDownX;
    private double _themePullPointerDownY;
    private double _themePullStartOffset;
    private double _themePullCurrentOffset = ThemePullRestY;
    private double _themePullTargetOffset = ThemePullRestY;
    private double _themePullVelocity;
    private bool _themePullDidEngage;
    private DispatcherTimer? _themePullSpringTimer;
    private EventHandler<object>? _railTravelHandler;

    public CompositionPage()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (this.Resources["NotepadSettle"] is Storyboard sb) sb.Begin();
        SyncThemePullTab();
        AttachToCurrentDataContextIfNeeded();
        ApplyRailVisibility();
        ApplyLayerVisibility();
        ApplyPreviewerVisibility(ReadActiveIndex());
        ApplyPreviewEditMode();
        StartPolling();
    }

    private void AttachToCurrentDataContextIfNeeded()
    {
        if (this.DataContext is INotifyPropertyChanged inpc && !ReferenceEquals(inpc, _bindable))
        {
            DetachBindable();
            _bindable = inpc;
            _activeIndexProperty = inpc.GetType().GetProperty("ActiveIndex");
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
    }

    private void StartPolling()
    {
        if (_pollTimer is not null) return;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();
    }

    private void StopPolling()
    {
        if (_pollTimer is null) return;
        _pollTimer.Tick -= OnPollTick;
        _pollTimer.Stop();
        _pollTimer = null;
    }

    private void OnPollTick(object? sender, object e)
    {
        AttachToCurrentDataContextIfNeeded();
        var idx = ReadActiveIndex();
        var indexChanged = idx != _lastSeenIndex;
        var isPreview = ReadIsPreviewMode() ?? true;
        var overrideMarkdown = ReadOverrideMarkdown();

        // The generated-markdown signature can only change when a composition
        // input changes — and every such mutation bumps CompositionRevision.
        // Reading the revision is one cheap int reflection read; only rebuild
        // the expensive snapshot signature (9+ reflection reads + a join) when
        // the revision, preview mode, or override state actually changed,
        // instead of reflecting the whole snapshot on every 150ms tick.
        var showsGenerated = isPreview && string.IsNullOrEmpty(overrideMarkdown);
        var revision = ReadCompositionRevision();
        string? generatedSignature;
        if (!showsGenerated)
        {
            generatedSignature = null;
        }
        else if (revision == _lastSeenRevision
                 && _lastSeenPreviewMode == isPreview
                 && string.Equals(_lastSeenOverrideMarkdown, overrideMarkdown, StringComparison.Ordinal)
                 && _lastSeenGeneratedMarkdownSignature is not null)
        {
            generatedSignature = _lastSeenGeneratedMarkdownSignature; // nothing changed — reuse
        }
        else
        {
            generatedSignature = BuildSnapshotSignature(BuildSnapshotFromProxy());
        }
        _lastSeenRevision = revision;
        var previewChanged = _lastSeenPreviewMode != isPreview
            || !string.Equals(_lastSeenOverrideMarkdown, overrideMarkdown, StringComparison.Ordinal)
            || !string.Equals(_lastSeenGeneratedMarkdownSignature, generatedSignature, StringComparison.Ordinal);
        if (indexChanged)
        {
            var previousIndex = _lastSeenIndex;
            _previousSeenIndex = previousIndex;
            _lastSeenIndex = idx;
            ApplyRailVisibility();
            ApplyLayerVisibility();
            RunLayerTransition();
            RunLeftRailDotTravel(previousIndex, idx);
            RunRightRailDotTransition(idx);
            // Settle the rail preview alongside the canvas so the two sides
            // move together instead of the rail snapping to new content.
            Motion.RunSettle(OverrideMarkdownView, fromY: 4, durationMs: 320);
            ScrollToTop();
        }
        if (indexChanged || previewChanged)
        {
            _lastSeenPreviewMode = isPreview;
            _lastSeenOverrideMarkdown = overrideMarkdown;
            _lastSeenGeneratedMarkdownSignature = generatedSignature;
            ApplyPreviewerVisibility(idx);
            ApplyPreviewEditMode();
        }
    }

    private void ApplyPreviewEditMode()
    {
        var isPreview = ReadIsPreviewMode() ?? true;
        var previewTarget = isPreview ? Visibility.Visible : Visibility.Collapsed;
        var editTarget    = isPreview ? Visibility.Collapsed : Visibility.Visible;
        var enteringEdit = !isPreview && EditModeTextBox.Visibility != Visibility.Visible;

        if (PreviewModeHost.Visibility != previewTarget) PreviewModeHost.Visibility = previewTarget;
        if (EditModeTextBox.Visibility != editTarget)     EditModeTextBox.Visibility = editTarget;

        ApplyPillSelection(isPreview);

        if (enteringEdit)
            SeedEditTextBoxFromBuffer();
    }

    private static readonly SolidColorBrush _transparentBrush =
        new(Microsoft.UI.Colors.Transparent);

    private void ApplyPillSelection(bool isPreview)
    {
        var inkBrush     = (Brush)Application.Current.Resources["InkBrush"];
        var ink2Brush    = (Brush)Application.Current.Resources["Ink2Brush"];
        var surfaceBrush = (Brush)Application.Current.Resources["NotepadSurfaceBrush"];
        var transparent  = _transparentBrush;

        PreviewPill.Background     = isPreview ? inkBrush     : transparent;
        PreviewPillText.Foreground = isPreview ? surfaceBrush : ink2Brush;

        EditPill.Background        = isPreview ? transparent  : inkBrush;
        EditPillText.Foreground    = isPreview ? ink2Brush    : surfaceBrush;
    }

    private void OnPillPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        var isPreview = ReadIsPreviewMode() ?? true;
        var isSelected = (fe == PreviewPill && isPreview) || (fe == EditPill && !isPreview);
        if (isSelected) return;
        if (sender is Border b)
            b.Background = (Brush)Application.Current.Resources["HairlineBrush"];
    }

    private void OnPillPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        ApplyPillSelection(ReadIsPreviewMode() ?? true);
    }

    private void SeedEditTextBoxFromBuffer()
    {
        var buffer = ReadEditBuffer() ?? string.Empty;
        _suppressEditTextChanged = true;
        try { EditModeTextBox.Text = buffer; }
        finally { _suppressEditTextChanged = false; }
    }

    private bool? ReadIsPreviewMode()
        => ProxyReader.ReadValue<bool>((object?)_bindable ?? this.DataContext, "IsPreviewMode");

    private int ReadCompositionRevision()
        => ProxyReader.ReadValue<int>((object?)_bindable ?? this.DataContext, "CompositionRevision") ?? 0;

    private string? ReadEditBuffer()
        => ProxyReader.Read<string>((object?)_bindable ?? this.DataContext, "EditBuffer");

    private void OnEditModeTextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        if (_suppressEditTextChanged) return;
        var dc = this.DataContext;
        if (dc is null) return;
        var prop = dc.GetType().GetProperty("EditBuffer");
        if (prop is null || !prop.CanWrite) return;
        try { prop.SetValue(dc, EditModeTextBox.Text); } catch { /* proxy may reject mid-transition */ }
    }

    private void ApplyPreviewerVisibility(int idx)
    {
        var overrideMarkdown = ReadOverrideMarkdown();
        var hasOverride = !string.IsNullOrEmpty(overrideMarkdown);
        var markdown = hasOverride
            ? overrideMarkdown
            : GetGeneratedMarkdown(LayerIdAt(idx));

        // Preview0..7 are x:Load=False (deferred, never instantiated) — the
        // single OverrideMarkdownView renders every layer. Nothing to collapse.
        SetLayerVisibility(OverrideMarkdownView, true);
        if (!string.Equals(OverrideMarkdownView.Markdown, markdown, StringComparison.Ordinal))
            OverrideMarkdownView.Markdown = markdown;
    }

    private string GetGeneratedMarkdown(string layerId)
    {
        var snapshot = BuildSnapshotFromProxy();
        var signature = BuildSnapshotSignature(snapshot);
        if (string.Equals(_generatedMarkdownLayerId, layerId, StringComparison.Ordinal) &&
            string.Equals(_generatedMarkdownSignature, signature, StringComparison.Ordinal) &&
            _generatedMarkdown is not null)
        {
            return _generatedMarkdown;
        }

        _generatedMarkdownLayerId = layerId;
        _generatedMarkdownSignature = signature;
        _generatedMarkdown = MarkdownGenerators.For(layerId, snapshot);
        return _generatedMarkdown;
    }

    private string? ReadOverrideMarkdown()
    {
        var activeLayer = ProxyReader.Read<LayerStatus>((object?)_bindable ?? this.DataContext, "ActiveLayer");
        return activeLayer?.OverrideMarkdown;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();

        _bindable = args.NewValue as INotifyPropertyChanged;
        _activeIndexProperty = args.NewValue?.GetType().GetProperty("ActiveIndex");

        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;

        ApplyRailVisibility();
        ApplyLayerVisibility();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachBindable();
        StopPolling();
        StopRenderingHandlers();
    }

    private void DetachBindable()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
        _activeIndexProperty = null;
    }

    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            ApplyRailVisibility();
            ApplyLayerVisibility();
            ApplyPreviewerVisibility(ReadActiveIndex());
            ApplyPreviewEditMode();
        });
    }

    private void ApplyLayerVisibility()
    {
        var idx = ReadActiveIndex();
        SetLayerVisibility(Layer0, idx == 0);
        SetLayerVisibility(Layer1, idx == 1);
        SetLayerVisibility(Layer2, idx == 2);
        SetLayerVisibility(Layer3, idx == 3);
        SetLayerVisibility(Layer4, idx == 4);
        SetLayerVisibility(Layer5, idx == 5);
        SetLayerVisibility(Layer6, idx == 6);
        SetLayerVisibility(Layer7, idx == 7);
    }

    private void SetLayerVisibility(FrameworkElement layer, bool visible)
    {
        var target = visible ? Visibility.Visible : Visibility.Collapsed;
        if (layer.Visibility != target) layer.Visibility = target;
        if (visible && this.DataContext is not null && !ReferenceEquals(layer.DataContext, this.DataContext))
            layer.DataContext = this.DataContext;
    }

    private void ScrollToTop()
    {
        if (CenterScroll is null) return;

        CenterScroll.ChangeView(null, 0, null, true);

        if (_scrollClampHandler is not null)
        {
            CompositionTarget.Rendering -= _scrollClampHandler;
            _scrollClampHandler = null;
        }

        var start = Environment.TickCount64;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (CenterScroll is null || Environment.TickCount64 - start > 600)
            {
                CompositionTarget.Rendering -= handler;
                _scrollClampHandler = null;
                return;
            }
            if (CenterScroll.VerticalOffset > 0.5)
                CenterScroll.ChangeView(null, 0, null, true);
        };
        _scrollClampHandler = handler;
        CompositionTarget.Rendering += handler;
    }

    private void RunLayerTransition()
    {
        var idx = ReadActiveIndex();
        FrameworkElement? layer = idx switch
        {
            0 => Layer0, 1 => Layer1, 2 => Layer2, 3 => Layer3,
            4 => Layer4, 5 => Layer5, 6 => Layer6, 7 => Layer7,
            _ => null,
        };
        if (layer is not null) Motion.RunSettle(layer, fromY: 4, durationMs: 320, fade: false);
    }

    private void ApplyRailVisibility()
    {
        ApplyExpandState(ReadActiveIndex() > 0);
    }

    private bool _tweenActive;
    private DateTime _tweenStart;
    private double _tweenFromMax, _tweenToMax;
    private double _tweenFromLeft, _tweenToLeft;
    private double _tweenFromRight, _tweenToRight;
    private bool _tweenExpand;

    private const int TweenMainDurationMs  = 720;
    private const int TweenRailDelayMs     = 100;
    private const int TweenRailDurationMs  = 560;
    private const int TweenTotalDurationMs = 720;

    private void ApplyExpandState(bool expand)
    {
        if (_expanded == expand) return;
        _expanded = expand;

        if (_tweenActive)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnTweenRender;
            _tweenActive = false;
        }

        _tweenFromMax   = SanitizeMaxWidth(NotepadFrame.MaxWidth, expand ? IntentMaxWidth : ExpandedMaxWidth);
        _tweenToMax     = expand ? ExpandedMaxWidth : IntentMaxWidth;
        _tweenFromLeft  = SanitizeWidth(LeftRail.Width, 0);
        _tweenToLeft    = expand ? LeftRailOpenWidth : 0;
        _tweenFromRight = SanitizeWidth(RightRail.Width, 0);
        _tweenToRight   = expand ? RightRailOpenWidth : 0;
        _tweenExpand    = expand;

        if (expand)
        {
            LeftRail.Visibility  = Visibility.Visible;
            RightRail.Visibility = Visibility.Visible;
            CenterContent.Width = 780;
        }
        else
        {
            CenterContent.Width = double.NaN;
        }

        _tweenStart = DateTime.UtcNow;
        _tweenActive = true;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnTweenRender;
    }

    private void OnTweenRender(object? sender, object e)
    {
        var elapsed = (DateTime.UtcNow - _tweenStart).TotalMilliseconds;

        var mainT = Clamp01(elapsed / TweenMainDurationMs);
        NotepadFrame.MaxWidth = Lerp(_tweenFromMax, _tweenToMax, EaseOutQuart(mainT));

        var railDelay = _tweenExpand ? TweenRailDelayMs : 0;
        var railT = Clamp01((elapsed - railDelay) / TweenRailDurationMs);
        var railEased = EaseOutQuart(railT);
        LeftRail.Width  = Lerp(_tweenFromLeft,  _tweenToLeft,  railEased);
        RightRail.Width = Lerp(_tweenFromRight, _tweenToRight, railEased);

        if (elapsed >= TweenTotalDurationMs)
        {
            NotepadFrame.MaxWidth = _tweenToMax;
            LeftRail.Width  = _tweenToLeft;
            RightRail.Width = _tweenToRight;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnTweenRender;
            _tweenActive = false;
            if (!_tweenExpand)
            {
                LeftRail.Visibility  = Visibility.Collapsed;
                RightRail.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
    private static double EaseOutQuart(double t) => 1.0 - Math.Pow(1.0 - t, 4);
    private static double SanitizeWidth(double v, double fallback) => double.IsNaN(v) || v < 0 ? fallback : v;
    private static double SanitizeMaxWidth(double v, double fallback) => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

    private int ReadActiveIndex()
    {
        if (_activeIndexProperty?.GetValue(_bindable) is int i) return i;
        return ProxyReader.ReadValue<int>(this.DataContext, "ActiveIndex") ?? 0;
    }

    private void OnRailItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe ||
            fe.DataContext is not Composer.Models.LayerStatus layer ||
            _bindable is null) return;

        MvuxCommandInvoker.Invoke(_bindable, "GoToLayer", layer.Index);
    }

    private void OnRailItemPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Direct, guaranteed-visible row tint (matches the pill hover pattern) —
        // the teal RailHoverSurface tween is subtle at 0.10 and depends on a
        // transform lookup that can no-op. The background tint always shows.
        if (sender is Border b) b.Background = (Brush)Application.Current.Resources["HairlineBrush"];
        TweenRailHover(sender as FrameworkElement, 5.0, 0.10, 1.0);
    }

    private void OnRailItemPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is Border b) b.Background = _transparentBrush;
        TweenRailHover(sender as FrameworkElement, 0.0, 0.0, 0.985);
    }

    private void TweenRailHover(FrameworkElement? row, double targetX, double targetOpacity, double targetScale)
    {
        if (row is null) return;
        var translate = FindDescendantByName(row, "ContentTranslate") as TranslateTransform;
        if (translate is null) return;
        var surface = FindDescendantByName(row, "RailHoverSurface") as Border;
        var scale = FindDescendantByName(row, "RailHoverScale") as ScaleTransform;

        var fromX = translate.X;
        var fromOpacity = surface?.Opacity ?? 0;
        var fromScale = scale?.ScaleX ?? targetScale;
        if (Math.Abs(fromX - targetX) < 0.01 &&
            Math.Abs(fromOpacity - targetOpacity) < 0.01 &&
            Math.Abs(fromScale - targetScale) < 0.001) return;

        if (_railHoverHandlers.TryGetValue(translate, out var existing))
        {
            CompositionTarget.Rendering -= existing;
            _railHoverHandlers.Remove(translate);
        }

        var start = Environment.TickCount64;
        const double durationMs = 160;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            var elapsed = Environment.TickCount64 - start;
            if (elapsed >= durationMs)
            {
                translate.X = targetX;
                if (surface is not null) surface.Opacity = targetOpacity;
                if (scale is not null)
                {
                    scale.ScaleX = targetScale;
                    scale.ScaleY = targetScale;
                }
                CompositionTarget.Rendering -= handler;
                _railHoverHandlers.Remove(translate);
                return;
            }
            var t = elapsed / durationMs;
            var eased = 1 - Math.Pow(1 - t, 4);
            translate.X = fromX + (targetX - fromX) * eased;
            if (surface is not null)
                surface.Opacity = fromOpacity + (targetOpacity - fromOpacity) * eased;
            if (scale is not null)
            {
                var currentScale = fromScale + (targetScale - fromScale) * eased;
                scale.ScaleX = currentScale;
                scale.ScaleY = currentScale;
            }
        };
        _railHoverHandlers[translate] = handler;
        CompositionTarget.Rendering += handler;
    }

    private readonly Dictionary<FrameworkElement, EventHandler<object>> _activeDotHandlers = new();

    private void OnActiveDotLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement host) return;
        OnActiveDotUnloaded(sender, e);

        var inner = FindDescendantByName(host, "ActiveDotInner") as Ellipse;
        var scale = FindDescendantByName(host, "ActiveDotScale") as ScaleTransform;
        if (inner is null || scale is null) return;

        var start = Environment.TickCount64;
        const double cycleMs = 4200;
        EventHandler<object> handler = (_, _) =>
        {
            var phase = ((Environment.TickCount64 - start) % (long)cycleMs) / cycleMs;
            var ping = 0.5 * (1 - Math.Cos(phase * 2 * Math.PI));
            var s = 1.0 - 0.08 * ping;
            scale.ScaleX = s;
            scale.ScaleY = s;
            inner.Opacity = 1.0 - 0.45 * ping;
        };
        _activeDotHandlers[host] = handler;
        CompositionTarget.Rendering += handler;
    }

    private void OnActiveDotUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement host) return;
        if (_activeDotHandlers.TryGetValue(host, out var handler))
        {
            CompositionTarget.Rendering -= handler;
            _activeDotHandlers.Remove(host);
        }
    }

    private void StopRenderingHandlers()
    {
        if (_tweenActive)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnTweenRender;
            _tweenActive = false;
        }
        if (_scrollClampHandler is not null)
        {
            CompositionTarget.Rendering -= _scrollClampHandler;
            _scrollClampHandler = null;
        }
        foreach (var handler in _railHoverHandlers.Values)
            CompositionTarget.Rendering -= handler;
        _railHoverHandlers.Clear();
        foreach (var handler in _activeDotHandlers.Values)
            CompositionTarget.Rendering -= handler;
        _activeDotHandlers.Clear();
        if (_railTravelHandler is not null)
        {
            CompositionTarget.Rendering -= _railTravelHandler;
            _railTravelHandler = null;
        }
    }

    private static DependencyObject? FindDescendantByName(DependencyObject root, string name)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name) return child;
            var deeper = FindDescendantByName(child, name);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    private static IEnumerable<FrameworkElement> FindDescendantsByName(DependencyObject root, string name)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe && fe.Name == name)
                yield return fe;
            foreach (var deeper in FindDescendantsByName(child, name))
                yield return deeper;
        }
    }

    private void RunLeftRailDotTravel(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex == toIndex || LeftRail.Visibility != Visibility.Visible)
            return;

        DispatcherQueue?.TryEnqueue(() =>
        {
            var fromHost = FindActiveDotHost(fromIndex);
            var toHost = FindActiveDotHost(toIndex);
            if (fromHost is null || toHost is null) return;

            var fromY = DotCenterY(fromHost);
            var toY = DotCenterY(toHost);
            if (double.IsNaN(fromY) || double.IsNaN(toY)) return;

            if (_railTravelHandler is not null)
            {
                CompositionTarget.Rendering -= _railTravelHandler;
                _railTravelHandler = null;
            }

            SetActiveDotScale(fromHost, 1);
            SetActiveDotScale(toHost, 0.62);
            RailTravelDot.Opacity = 1;
            RailTravelDotTransform.TranslateY = fromY - (RailTravelDot.Height / 2);
            RailTravelDotTransform.ScaleX = 1;
            RailTravelDotTransform.ScaleY = 1;

            var start = Environment.TickCount64;
            const double durationMs = 420;
            _railTravelHandler = (_, _) =>
            {
                var t = Math.Min(1, (Environment.TickCount64 - start) / durationMs);
                var moveT = EaseInOut(t);
                var shrinkT = Clamp01(t / 0.28);
                var growT = Clamp01((t - 0.56) / 0.44);
                var y = Lerp(fromY, toY, moveT);
                var travelScale = t < 0.35
                    ? Lerp(1.0, 0.56, EaseOutQuart(shrinkT))
                    : Lerp(0.56, 1.0, EaseOutQuart(growT));

                SetActiveDotScale(fromHost, Lerp(1.0, 0.52, EaseOutQuart(shrinkT)));
                SetActiveDotScale(toHost, Lerp(0.62, 1.0, EaseOutQuart(growT)));
                RailTravelDotTransform.TranslateY = y - (RailTravelDot.Height / 2);
                RailTravelDotTransform.ScaleX = travelScale;
                RailTravelDotTransform.ScaleY = travelScale;
                RailTravelDot.Opacity = t < 0.82 ? 1 : Lerp(1, 0, Clamp01((t - 0.82) / 0.18));

                if (t >= 1)
                {
                    SetActiveDotScale(fromHost, 1);
                    SetActiveDotScale(toHost, 1);
                    RailTravelDot.Opacity = 0;
                    RailTravelDotTransform.ScaleX = 1;
                    RailTravelDotTransform.ScaleY = 1;
                    CompositionTarget.Rendering -= _railTravelHandler;
                    _railTravelHandler = null;
                }
            };
            CompositionTarget.Rendering += _railTravelHandler;
        });
    }

    private FrameworkElement? FindActiveDotHost(int index)
        => FindDescendantsByName(LeftRail, "ActiveDotHost")
            .FirstOrDefault(fe => fe.DataContext is LayerStatus layer && layer.Index == index);

    private static void SetActiveDotScale(FrameworkElement host, double scaleValue)
    {
        if (FindDescendantByName(host, "ActiveDotScale") is ScaleTransform scale)
        {
            scale.ScaleX = scaleValue;
            scale.ScaleY = scaleValue;
        }
    }

    private double DotCenterY(FrameworkElement host)
    {
        var transform = host.TransformToVisual(LeftRail);
        var p = transform.TransformPoint(new Windows.Foundation.Point(host.ActualWidth / 2, host.ActualHeight / 2));
        return p.Y;
    }

    private static double EaseInOut(double t)
    {
        t = Clamp01(t);
        return t < 0.5
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    private void RunRightRailDotTransition(int activeIndex)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            var dot = FindDescendantsByName(RightRail, "FileStatusDot")
                .FirstOrDefault(fe => fe.DataContext is LayerStatus layer && layer.Index == activeIndex);
            if (dot is null) return;

            if (dot.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform();
                dot.RenderTransform = scale;
            }

            var storyboard = new Storyboard();

            var scaleX = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(scaleX, scale);
            Storyboard.SetTargetProperty(scaleX, "ScaleX");
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.7 });
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.55, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(310), Value = 1.0, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            var scaleY = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(scaleY, scale);
            Storyboard.SetTargetProperty(scaleY, "ScaleY");
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.7 });
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(150), Value = 1.55, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(310), Value = 1.0, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            var opacity = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(opacity, dot);
            Storyboard.SetTargetProperty(opacity, "Opacity");
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 0.55 });
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(120), Value = 1.0 });
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromMilliseconds(310), Value = 1.0 });

            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(opacity);
            storyboard.Begin();
        });
    }

    private void OnCopyActiveMarkdownClicked(object sender, RoutedEventArgs e)
    {
        var layerId = LayerIdAt(ReadActiveIndex());
        var md = Composer.Models.MarkdownGenerators.For(layerId, BuildSnapshotFromProxy());
        CopyToClipboard(md);
    }

    private ComposerSnapshot BuildSnapshotFromProxy()
    {
        var platforms = ProxyReader.ReadHashSet<PlatformKind>(_bindable, "SelectedPlatforms");
        if (platforms.Count == 0)
            platforms = ImmutableHashSet.Create(PlatformKind.Web, PlatformKind.Android, PlatformKind.iOS);

        return new ComposerSnapshot(
            ReadIntent() ?? Intent.Example,
            ProxyReader.ReadDesignTokens(_bindable),
            platforms,
            ProxyReader.ReadValue<RuntimeKind>(_bindable, "SelectedRuntime") ?? RuntimeKind.Net10,
            ProxyReader.Read<string>(_bindable, "IntentOverview") ?? string.Empty,
            ProxyReader.ReadArray<string>(_bindable, "ReferenceScreenshotPaths"));
    }

    private static string BuildSnapshotSignature(ComposerSnapshot snapshot)
    {
        var platforms = string.Join(",", snapshot.Platforms.OrderBy(p => (int)p));
        var refs = snapshot.ReferenceScreenshots.IsDefaultOrEmpty ? "0" : string.Join("|", snapshot.ReferenceScreenshots);
        var i = snapshot.Intent;
        var d = snapshot.Design;
        return string.Join("\u001F",
            i.AppType, i.PrimaryUser, i.Workflow, i.Platforms,
            d.Surface, d.Action, d.Info, d.Success, d.Warn, d.Panel, d.Tag, d.Locked, d.BodyFont,
            platforms, snapshot.Runtime.ToString(), snapshot.IntentOverview, refs);
    }

    private Composer.Models.Intent ReadIntent()
        => ProxyReader.ReadIntent(_bindable);

    private static string LayerIdAt(int idx) => idx switch
    {
        0 => "intent",
        1 => "ux",
        2 => "architecture",
        3 => "design",
        4 => "interactions",
        5 => "data",
        6 => "implementation",
        7 => "scaffold",
        _ => "intent",
    };

    internal static void CopyToClipboard(string text)
    {
        var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
    }

    private void OnPreviewPillTapped(object sender, TappedRoutedEventArgs e)
        => InvokeProxyCommand("EnablePreview");

    private void OnEditPillTapped(object sender, TappedRoutedEventArgs e)
        => InvokeProxyCommand("EnableEdit");

    private void InvokeProxyCommand(string commandName)
    {
        var target = (object?)_bindable ?? this.DataContext;
        MvuxCommandInvoker.Invoke(target, commandName);
    }

    private void SyncThemePullTab()
    {
        var rootTheme = XamlRoot?.Content is FrameworkElement root && root.RequestedTheme != ElementTheme.Default
            ? root.RequestedTheme
            : RequestedTheme;
        var isDark = rootTheme != ElementTheme.Light;
        ApplyRequestedTheme(isDark ? ElementTheme.Dark : ElementTheme.Light);
        ThemePullGlyph.Text = isDark ? "DARK" : "LIGHT";
        SetThemePullOffset(ThemePullRestY);
    }

    private void OnThemePullTabPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        StopThemePullSpring();
        _isThemePullDragging = true;
        var p = e.GetCurrentPoint(this).Position;
        _themePullStartPointerY = p.Y;
        _themePullPointerDownX = p.X;
        _themePullPointerDownY = p.Y;
        _themePullStartOffset = _themePullCurrentOffset;
        _themePullVelocity = 0;
        _themePullTargetOffset = ThemePullRestY;
        _themePullDidEngage = false;
        ThemePullTab.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnThemePullTabPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isThemePullDragging) return;
        var y = e.GetCurrentPoint(this).Position.Y;
        var rawPull = _themePullStartOffset + Math.Max(0, y - _themePullStartPointerY);
        SetThemePullOffset(ApplyThemePullResistance(rawPull));
        CheckThemePullEngagement();
        e.Handled = true;
    }

    private void OnThemePullTabPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isThemePullDragging) return;
        var p = e.GetCurrentPoint(this).Position;
        var moved = Math.Sqrt(Math.Pow(p.X - _themePullPointerDownX, 2) + Math.Pow(p.Y - _themePullPointerDownY, 2)) >= 4;
        _isThemePullDragging = false;
        ThemePullTab.ReleasePointerCapture(e.Pointer);
        if (!moved)
        {
            _themePullDidEngage = false;
            _themePullVelocity = 4.5;
            _themePullTargetOffset = ThemePullTapTarget;
            StartThemePullSpring();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _themePullTargetOffset = ThemePullRestY;
                StartThemePullSpring();
            };
            timer.Start();
        }
        else
        {
            _themePullTargetOffset = ThemePullRestY;
            StartThemePullSpring();
        }
        e.Handled = true;
    }

    private void OnThemePullTabPointerCanceled(object sender, PointerRoutedEventArgs e)
        => CancelThemePull(e.Pointer);

    private void OnThemePullTabPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        => CancelThemePull(e.Pointer);

    private void CancelThemePull(Microsoft.UI.Xaml.Input.Pointer pointer)
    {
        if (!_isThemePullDragging) return;
        _isThemePullDragging = false;
        _themePullTargetOffset = ThemePullRestY;
        StartThemePullSpring();
    }

    private static double ApplyThemePullResistance(double pull)
    {
        if (pull <= ThemePullMax) return Math.Max(ThemePullRestY, pull);
        return ThemePullMax + ((pull - ThemePullMax) * 0.25);
    }

    private void ToggleThemeFromPull()
    {
        var isDark = RequestedTheme != ElementTheme.Light;
        var next = isDark ? ElementTheme.Light : ElementTheme.Dark;
        ApplyRequestedTheme(next);
        ThemePullGlyph.Text = next == ElementTheme.Dark ? "DARK" : "LIGHT";
    }

    private void SetThemePullOffset(double offset)
    {
        _themePullCurrentOffset = Math.Max(ThemePullRestY, offset);
        ThemePullTabTransform.Y = 0;
        ThemePullCordScale.ScaleY = _themePullCurrentOffset;
        ThemePullHandleTransform.Y = _themePullCurrentOffset - 2;
        ThemePullTab.Opacity = 0.78 + Math.Min((_themePullCurrentOffset - ThemePullRestY) / (ThemePullThreshold - ThemePullRestY), 1) * 0.22;
    }

    private void CheckThemePullEngagement()
    {
        if (_themePullDidEngage || _themePullCurrentOffset < ThemePullThreshold) return;
        _themePullDidEngage = true;
        ToggleThemeFromPull();
    }

    private void StartThemePullSpring()
    {
        StopThemePullSpring();
        _themePullSpringTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _themePullSpringTimer.Tick += OnThemePullSpringTick;
        _themePullSpringTimer.Start();
    }

    private void StopThemePullSpring()
    {
        if (_themePullSpringTimer is null) return;
        _themePullSpringTimer.Tick -= OnThemePullSpringTick;
        _themePullSpringTimer.Stop();
        _themePullSpringTimer = null;
        _themePullVelocity = 0;
    }

    private void OnThemePullSpringTick(object? sender, object e)
    {
        const double stiffness = 0.32;
        const double damping = 0.55;
        var force = (_themePullTargetOffset - _themePullCurrentOffset) * stiffness;
        _themePullVelocity = (_themePullVelocity + force) * damping;
        var next = _themePullCurrentOffset + _themePullVelocity;
        SetThemePullOffset(next);
        CheckThemePullEngagement();

        if (Math.Abs(_themePullCurrentOffset - _themePullTargetOffset) < 0.2 && Math.Abs(_themePullVelocity) < 0.2)
        {
            SetThemePullOffset(_themePullTargetOffset);
            if (Math.Abs(_themePullTargetOffset - ThemePullRestY) < 0.1)
                _themePullDidEngage = false;
            StopThemePullSpring();
            return;
        }
    }

    private void ApplyRequestedTheme(ElementTheme theme)
    {
        RequestedTheme = theme;
        if (XamlRoot?.Content is FrameworkElement root)
            root.RequestedTheme = theme;
    }
}
