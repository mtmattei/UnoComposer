using System;
using System.Collections;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class ComposerFooter : UserControl
{
    private INotifyPropertyChanged? _bindable;

    private DispatcherTimer? _pollTimer;
    private bool _lastSeenGenerating;
    private bool _lastSeenAccepting;
    private FrameworkElement? _visibilityRoot;
    private long _visibilityRootToken;

    private TextScrambler? _generateScrambler;
    private TextScrambler? _acceptScrambler;
    private Storyboard? _discardFadeStoryboard;
    private Storyboard? _refinementRevealStoryboard;
    private DispatcherTimer? _questionRevealTimer;
    private bool _discardHiddenByAccept;
    private bool? _lastRefinementPanelVisible;
    private string _questionRevealTarget = string.Empty;
    private int _questionRevealIndex;
    private int _questionRevealFrame;
    private string? _lastChipSignature;
    private string? _lastMissingChipSignature;
    private static readonly char[] BrailleRevealPool =
        { '⠀','⣀','⣤','⣴','⣶','⣷','⣿','⣶','⣴','⣤','⣀','⠋','⠙','⠹','⠽','⠿' };
    private TextScrambler GenerateScrambler => _generateScrambler ??= new TextScrambler(GenerateLabel);
    private TextScrambler AcceptScrambler   => _acceptScrambler   ??= new TextScrambler(AcceptLabel);

    public ComposerFooter()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        AttachVisibilityRoot();
        SyncPollingWithVisibility();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopPolling();
        _generateScrambler?.Cancel();
        _acceptScrambler?.Cancel();
        DetachVisibilityRoot();
        DetachBindable();
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

    private void SyncPollingWithVisibility()
    {
        if (IsVisibleInTree())
        {
            StartPolling();
            Refresh();
        }
        else
        {
            StopPolling();
            _generateScrambler?.Cancel();
            _acceptScrambler?.Cancel();
        }
    }

    private void OnPollTick(object? sender, object e)
    {
        if (!IsVisibleInTree()) return;

        var generating = IsCommandExecuting("GeneratePreview");
        var accepting  = IsCommandExecuting("AcceptAndLock");
        if (generating == _lastSeenGenerating && accepting == _lastSeenAccepting) return;
        _lastSeenGenerating = generating;
        _lastSeenAccepting  = accepting;
        Refresh();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        if (IsVisibleInTree()) Refresh();
    }

    private void DetachBindable()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
    }

    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsVisibleInTree()) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (IsVisibleInTree()) Refresh();
        });
    }

    private void Refresh()
    {
        if (!IsVisibleInTree()) return;

        var state = ReadLayerState() ?? LayerState.Clean;
        var layerId = ReadActiveLayerId();
        var isIntent = layerId == "intent";

        var overview = isIntent
            ? (ProxyReader.Read<string>(_bindable, "IntentOverview") ?? string.Empty)
            : string.Empty;
        var hasOverview = !string.IsNullOrWhiteSpace(overview);

        var questionText = isIntent
            ? (hasOverview ? overview : LayerPrompts.IntentQuestion(state))
            : (ReadProperty<string>("ActiveLayerPrompt", "Question") ?? string.Empty);
        if (_questionRevealTimer is null)
            QuestionText.Text = questionText;

        RefreshChips();
        RefreshMissingSectionChips(layerId);
        ApplyRefinementPanelVisibility(isIntent, hasOverview, state, questionText);
        ApplyStateVisibility(state, layerId, hasOverview);
        ApplyCommandExecutingState(layerId, state, hasOverview);
    }

    private void ApplyRefinementPanelVisibility(bool isIntent, bool hasOverview, LayerState state, string questionText)
    {
        var shouldShow = !isIntent || hasOverview;
        if (_lastRefinementPanelVisible == shouldShow) return;
        _lastRefinementPanelVisible = shouldShow;

        _refinementRevealStoryboard?.Stop();
        StopQuestionReveal(finalText: shouldShow ? questionText : string.Empty);

        if (!shouldShow)
        {
            RefinementPanel.Visibility = Visibility.Collapsed;
            RefinementPanel.Opacity = 0;
            RefinementPanelTransform.Y = -8;
            return;
        }

        RefinementPanel.Visibility = Visibility.Visible;

        var opacity = new DoubleAnimation
        {
            From = RefinementPanel.Opacity <= 0 ? 0 : RefinementPanel.Opacity,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacity, RefinementPanel);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var slide = new DoubleAnimation
        {
            From = RefinementPanelTransform.Y == 0 ? -8 : RefinementPanelTransform.Y,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, RefinementPanelTransform);
        Storyboard.SetTargetProperty(slide, "Y");

        _refinementRevealStoryboard = new Storyboard();
        _refinementRevealStoryboard.Children.Add(opacity);
        _refinementRevealStoryboard.Children.Add(slide);
        _refinementRevealStoryboard.Begin();

        if (isIntent && !string.IsNullOrWhiteSpace(questionText))
            StartQuestionReveal(questionText);
    }

    private void StartQuestionReveal(string text)
    {
        StopQuestionReveal(finalText: string.Empty);
        _questionRevealTarget = text;
        _questionRevealIndex = 0;
        _questionRevealFrame = 0;
        QuestionText.Text = string.Empty;
        _questionRevealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _questionRevealTimer.Tick += OnQuestionRevealTick;
        _questionRevealTimer.Start();
    }

    private void StopQuestionReveal(string finalText)
    {
        if (_questionRevealTimer is not null)
        {
            _questionRevealTimer.Tick -= OnQuestionRevealTick;
            _questionRevealTimer.Stop();
            _questionRevealTimer = null;
        }
        if (!string.IsNullOrEmpty(finalText))
            QuestionText.Text = finalText;
    }

    private void OnQuestionRevealTick(object? sender, object e)
    {
        if (string.IsNullOrEmpty(_questionRevealTarget))
        {
            StopQuestionReveal(finalText: string.Empty);
            return;
        }

        _questionRevealFrame++;
        if (_questionRevealFrame % 2 == 0)
            _questionRevealIndex++;

        if (_questionRevealIndex >= _questionRevealTarget.Length)
        {
            StopQuestionReveal(finalText: _questionRevealTarget);
            return;
        }

        var committed = _questionRevealTarget.Substring(0, _questionRevealIndex);
        var next = BrailleRevealPool[_questionRevealFrame % BrailleRevealPool.Length];
        QuestionText.Text = committed + next;
    }

    private void RefreshMissingSectionChips(string? layerId)
    {
        var coverage = ProxyReader.Read<LayerCoverage>(_bindable, "ActiveLayerCoverage");
        if (coverage is null || coverage.Total == 0 || coverage.IsComplete)
        {
            if (_lastMissingChipSignature is not null)
            {
                MissingChipsHost.Children.Clear();
                MissingSectionsRow.Visibility = Visibility.Collapsed;
                _lastMissingChipSignature = null;
            }
            return;
        }

        var missing = coverage.Missing;
        var signature = string.Join("", missing.Select(s => s.CoveragePrompt));
        if (string.Equals(signature, _lastMissingChipSignature, StringComparison.Ordinal)) return;
        _lastMissingChipSignature = signature;

        MissingChipsHost.Children.Clear();
        var chipStyle = Application.Current.Resources["SuggestionChipStyle"] as Style;
        var refineCmd = ReadCommand("RefineSection");
        foreach (var spec in missing)
        {
            MissingChipsHost.Children.Add(new Button
            {
                Style = chipStyle,
                Content = spec.CoveragePrompt,
                Command = refineCmd,
                CommandParameter = spec.CoveragePrompt,
            });
        }
        MissingSectionsRow.Visibility = Visibility.Visible;
    }

    private void RefreshChips()
    {
        var prompt = ReadValue("ActiveLayerPrompt");
        if (prompt is null)
        {
            if (_lastChipSignature is not null)
            {
                ChipsHost.Children.Clear();
                _lastChipSignature = null;
            }
            return;
        }

        var suggestions = prompt.GetType().GetProperty("Suggestions")?.GetValue(prompt) as IEnumerable;
        if (suggestions is null)
        {
            if (_lastChipSignature is not null)
            {
                ChipsHost.Children.Clear();
                _lastChipSignature = null;
            }
            return;
        }

        var values = new List<string>();
        foreach (var item in suggestions)
            if (item is string s) values.Add(s);

        var signature = string.Join("\u001F", values);
        if (string.Equals(_lastChipSignature, signature, StringComparison.Ordinal)) return;
        _lastChipSignature = signature;

        ChipsHost.Children.Clear();
        var chipStyle = Application.Current.Resources["SuggestionChipStyle"] as Style;
        var useCommand = ReadCommand("UsePrompt");
        foreach (var s in values)
        {
            var btn = new Button
            {
                Style = chipStyle,
                Content = s,
                Command = useCommand,
                CommandParameter = s,
            };
            ChipsHost.Children.Add(btn);
        }
    }

    private void ApplyStateVisibility(LayerState state, string? layerId, bool hasOverview)
    {
        var isIntent = layerId == "intent";

        if (isIntent)
        {
            ContinueBtn.Visibility = Visibility.Collapsed;
            GenerateBtn.Visibility = Visibility.Visible;
            AcceptBtn.Visibility   = hasOverview || state == LayerState.Previewing ? Visibility.Visible : Visibility.Collapsed;
            DiscardBtn.Visibility  = state != LayerState.Clean ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            ContinueBtn.Visibility = state == LayerState.Clean      ? Visibility.Visible : Visibility.Collapsed;
            GenerateBtn.Visibility = state is LayerState.Dirty or LayerState.Previewing ? Visibility.Visible : Visibility.Collapsed;
            AcceptBtn.Visibility   = state == LayerState.Previewing ? Visibility.Visible : Visibility.Collapsed;
            DiscardBtn.Visibility  = state != LayerState.Clean      ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ApplyCommandExecutingState(string? layerId, LayerState state, bool hasOverview)
    {
        var generating = IsCommandExecuting("GeneratePreview");
        var accepting  = IsCommandExecuting("AcceptAndLock");
        var isIntent   = layerId == "intent";

        var generateLabel = isIntent
            ? (hasOverview ? "Regenerate overview →" : "Generate overview →")
            : (state == LayerState.Previewing ? "Regenerate preview →" : "Generate preview →");
        var generatingLabel = isIntent ? "Generating overview" : "Generating preview";
        var acceptLabel = isIntent ? "Accept & Continue →" : "Accept and lock →";

        if (generating)
            GenerateScrambler.Start(generatingLabel);
        else
            GenerateScrambler.Stop(generateLabel);
        GenerateBtn.IsEnabled = !generating;

        if (accepting)
            AcceptScrambler.Start("Locking");
        else
            AcceptScrambler.Stop(acceptLabel);
        AcceptBtn.IsEnabled = !accepting;

        DiscardBtn.IsEnabled  = !(generating || accepting);
        ApplyDiscardAcceptFade(accepting);
    }

    private void ApplyDiscardAcceptFade(bool accepting)
    {
        if (accepting && DiscardBtn.Visibility == Visibility.Visible)
        {
            _discardHiddenByAccept = true;
            AnimateDiscardOpacity(0, collapseOnComplete: true);
            return;
        }

        if (!accepting && _discardHiddenByAccept)
        {
            _discardHiddenByAccept = false;
            if (DiscardBtn.Visibility == Visibility.Visible)
                AnimateDiscardOpacity(1, collapseOnComplete: false);
            else
                DiscardBtn.Opacity = 1;
        }
        else if (!accepting && DiscardBtn.Visibility == Visibility.Visible && DiscardBtn.Opacity < 1)
        {
            AnimateDiscardOpacity(1, collapseOnComplete: false);
        }
    }

    private void AnimateDiscardOpacity(double to, bool collapseOnComplete)
    {
        _discardFadeStoryboard?.Stop();
        if (!collapseOnComplete && DiscardBtn.Visibility != Visibility.Visible)
            DiscardBtn.Visibility = Visibility.Visible;

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, DiscardBtn);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        if (collapseOnComplete)
        {
            storyboard.Completed += (_, _) =>
            {
                if (_discardHiddenByAccept)
                    DiscardBtn.Visibility = Visibility.Collapsed;
            };
        }
        _discardFadeStoryboard = storyboard;
        storyboard.Begin();
    }

    private string? ReadActiveLayerId()
    {
        var idx = ReadValue("ActiveIndex") is int i ? i : 0;
        if (ReadValue("Layers") is not IEnumerable layers) return null;
        int seen = 0;
        foreach (var item in layers)
        {
            if (seen == idx)
                return item?.GetType().GetProperty("Id")?.GetValue(item) as string;
            seen++;
        }
        return null;
    }

    private bool IsCommandExecuting(string commandName)
    {
        if (_bindable is null) return false;
        var cmd = _bindable.GetType().GetProperty(commandName)?.GetValue(_bindable);
        var prop = cmd?.GetType().GetProperty("IsExecuting");
        return prop?.GetValue(cmd) is bool b && b;
    }

    private LayerState? ReadLayerState()
    {
        var idx = ReadValue("ActiveIndex") is int i ? i : 0;
        if (ReadValue("Layers") is not IEnumerable layers) return null;
        int seen = 0;
        foreach (var item in layers)
        {
            if (seen == idx)
            {
                var prop = item?.GetType().GetProperty("State");
                return prop?.GetValue(item) as LayerState?;
            }
            seen++;
        }
        return null;
    }

    private object? ReadValue(string propertyName)
        => _bindable?.GetType().GetProperty(propertyName)?.GetValue(_bindable);

    private T? ReadProperty<T>(string ownerPropertyName, string innerPropertyName) where T : class
    {
        var owner = ReadValue(ownerPropertyName);
        if (owner is null) return null;
        return owner.GetType().GetProperty(innerPropertyName)?.GetValue(owner) as T;
    }

    private ICommand? ReadCommand(string commandName)
        => _bindable?.GetType().GetProperty(commandName)?.GetValue(_bindable) as ICommand;

    private void AttachVisibilityRoot()
    {
        if (_visibilityRoot is not null) return;
        var current = VisualTreeHelper.GetParent(this);
        while (current is not null)
        {
            if (current is UserControl uc && !ReferenceEquals(uc, this))
            {
                _visibilityRoot = uc;
                _visibilityRootToken = uc.RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityRootChanged);
                return;
            }
            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void DetachVisibilityRoot()
    {
        if (_visibilityRoot is not null && _visibilityRootToken != 0)
            _visibilityRoot.UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityRootToken);
        _visibilityRoot = null;
        _visibilityRootToken = 0;
    }

    private void OnVisibilityRootChanged(DependencyObject sender, DependencyProperty dp)
        => SyncPollingWithVisibility();

    private bool IsVisibleInTree()
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is UIElement { Visibility: Visibility.Collapsed })
                return false;
            current = VisualTreeHelper.GetParent(current);
        }
        return true;
    }

    private void OnComposerPromptChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
            MvuxCommandInvoker.Invoke(DataContext, "MarkActiveDirty");
    }

    private sealed class TextScrambler
    {
        private static readonly char[] BraillePool =
            { '⠀','⣀','⣤','⣴','⣶','⣷','⣿','⣶','⣴','⣤','⣀','⠋','⠙','⠹','⠽','⠿' };

        private readonly TextBlock _host;
        private DispatcherTimer? _timer;
        private int _frame;
        private string _target = string.Empty;
        private FontFamily? _originalFont;

        public TextScrambler(TextBlock host) { _host = host; }

        public bool IsRunning => _timer is not null;

        public void Start(string target)
        {
            if (IsRunning)
            {
                _target = target;
                return;
            }
            _target = target;
            _frame = 0;
            _originalFont = _host.FontFamily;
            try { _host.FontFamily = (FontFamily)Application.Current.Resources["MonoFontFamily"]; }
            catch { /* resource may not be ready on first paint — degrade gracefully */ }
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            _timer.Tick += OnTick;
            _timer.Start();
            Render();
        }

        public void Stop(string finalText)
        {
            CancelTimer();
            _host.Text = finalText;
        }

        public void Cancel() => CancelTimer();

        private void CancelTimer()
        {
            if (_timer is null) return;
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
            if (_originalFont is not null)
            {
                _host.FontFamily = _originalFont;
                _originalFont = null;
            }
        }

        private void OnTick(object? sender, object e)
        {
            _frame++;
            Render();
        }

        private void Render()
        {
            var sb = new StringBuilder(_target.Length);
            for (int i = 0; i < _target.Length; i++)
            {
                var ch = _target[i];
                if (ch is ' ' or '→' or '…' or '.' or ',')
                {
                    sb.Append(ch);
                    continue;
                }
                var idx = (_frame * 3 + i * 7) % BraillePool.Length;
                sb.Append(BraillePool[idx]);
            }
            _host.Text = sb.ToString();
        }
    }
}
