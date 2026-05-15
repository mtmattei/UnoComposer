using System;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Composer.Presentation.Controls;

/// <summary>
/// Single-select chip for the .NET runtime row. Visually mirrors
/// <see cref="PlatformChip"/>. Single-select semantics are enforced by the
/// parent (the page wires <see cref="Toggled"/> to flip the other chip off).
/// </summary>
public sealed class RuntimeChip : Control
{
    public static readonly DependencyProperty RuntimeKindProperty =
        DependencyProperty.Register(
            nameof(RuntimeKind), typeof(RuntimeKind), typeof(RuntimeChip),
            new PropertyMetadata(RuntimeKind.Net10));

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected), typeof(bool), typeof(RuntimeChip),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(RuntimeChip), new PropertyMetadata(null));

    public RuntimeKind RuntimeKind
    {
        get => (RuntimeKind)GetValue(RuntimeKindProperty);
        set => SetValue(RuntimeKindProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    // Optional ICommand fired on user selection. Parameter is RuntimeKind.
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public event EventHandler<bool>? Toggled;

    private bool _pointerInside;

    public RuntimeChip()
    {
        DefaultStyleKey = typeof(RuntimeChip);
        IsTabStop = true;

        PointerPressed  += (_, _) => VisualStateManager.GoToState(this, "Pressed", MotionPreferences.AnimationsEnabled);
        PointerReleased += (_, _) =>
        {
            // Single-select: clicking the already-selected chip is a no-op.
            if (!IsSelected)
            {
                IsSelected = true;
                Toggled?.Invoke(this, true);
                if (Command is { } cmd && cmd.CanExecute(RuntimeKind))
                    cmd.Execute(RuntimeKind);
            }
            VisualStateManager.GoToState(this, _pointerInside ? "PointerOver" : "Normal", MotionPreferences.AnimationsEnabled);
        };
        PointerEntered += (_, _) =>
        {
            _pointerInside = true;
            VisualStateManager.GoToState(this, "PointerOver", MotionPreferences.AnimationsEnabled);
        };
        PointerExited += (_, _) =>
        {
            _pointerInside = false;
            VisualStateManager.GoToState(this, "Normal", MotionPreferences.AnimationsEnabled);
        };
        PointerCaptureLost += (_, _) =>
        {
            _pointerInside = false;
            VisualStateManager.GoToState(this, "Normal", MotionPreferences.AnimationsEnabled);
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is VirtualKey.Space or VirtualKey.Enter)
            {
                if (!IsSelected)
                {
                    IsSelected = true;
                    Toggled?.Invoke(this, true);
                    if (Command is { } cmd && cmd.CanExecute(RuntimeKind))
                        cmd.Execute(RuntimeKind);
                }
                e.Handled = true;
            }
        };
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", false);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RuntimeChip chip)
            VisualStateManager.GoToState(chip, chip.IsSelected ? "Selected" : "Unselected", MotionPreferences.AnimationsEnabled);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RuntimeChipAutomationPeer(this);

    internal sealed class RuntimeChipAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider
    {
        public RuntimeChipAutomationPeer(RuntimeChip owner) : base(owner) { }

        public bool IsSelected => ((RuntimeChip)Owner).IsSelected;
        public IRawElementProviderSimple? SelectionContainer => null;

        public void AddToSelection()
        {
            var chip = (RuntimeChip)Owner;
            if (!chip.IsSelected)
            {
                chip.IsSelected = true;
                chip.Toggled?.Invoke(chip, true);
                if (chip.Command is { } cmd && cmd.CanExecute(chip.RuntimeKind))
                    cmd.Execute(chip.RuntimeKind);
            }
        }

        public void RemoveFromSelection() { /* runtime is required-one — no-op */ }
        public void Select() => AddToSelection();

        protected override AutomationControlType GetAutomationControlTypeCore()
            => AutomationControlType.RadioButton;

        protected override object GetPatternCore(PatternInterface patternInterface)
            => patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);

        protected override string GetNameCore() => ((RuntimeChip)Owner).RuntimeKind.DisplayName();
    }
}
