using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class ScaffoldLayerView : UserControl
{
    private static readonly SolidColorBrush CommandBrush  = new(Color.FromArgb(0xFF, 0xA4, 0xC9, 0x7D));
    private static readonly SolidColorBrush DefaultBrush  = new(Color.FromArgb(0xFF, 0xE4, 0xE4, 0xE4));

    private INotifyPropertyChanged? _bindable;

    public ScaffoldLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += (_, _) => DetachBindable();
        // Re-refresh whenever this layer flips to Visible — see UxLayerView.
        this.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) =>
        {
            if (Visibility == Visibility.Visible) RebuildScaffoldCommand();
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        if (Visibility == Visibility.Visible) RebuildScaffoldCommand();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        if (Visibility == Visibility.Visible) RebuildScaffoldCommand();
    }

    private void DetachBindable()
    {
        if (_bindable is null) return;
        _bindable.PropertyChanged -= OnBindablePropertyChanged;
        _bindable = null;
    }

    private void OnBindablePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Visibility != Visibility.Visible) return;
        DispatcherQueue?.TryEnqueue(() =>
        {
            if (Visibility == Visibility.Visible) RebuildScaffoldCommand();
        });
    }

    private void RebuildScaffoldCommand()
    {
        var snap = ResolveSnapshot();
        var ctx = IntentContext.DeriveFrom(snap.Intent);
        var cmd = MarkdownGenerators.BuildScaffoldCommand(snap, ctx);

        ScaffoldCommandText.Inlines.Clear();
        // First line is the actual `dotnet new` invocation — highlighted
        // green to draw the eye. Rest of the lines stay default-coloured.
        var lines = cmd.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var brush = i == 0 ? CommandBrush : DefaultBrush;
            ScaffoldCommandText.Inlines.Add(new Run { Text = lines[i], Foreground = brush });
            if (i < lines.Length - 1)
                ScaffoldCommandText.Inlines.Add(new LineBreak());
        }
    }

    private ComposerSnapshot ResolveSnapshot()
    {
        var proxy = this.DataContext;
        var intent = ProxyReader.ReadIntent(proxy);
        var design = ProxyReader.ReadDesignTokens(proxy);
        // ReadHashSet iterates the proxy's collection robustly — Read<T>
        // returns null for SelectedPlatforms (the wrapper exposes the set
        // through an interface type rather than the concrete
        // ImmutableHashSet<T>), which previously caused the snapshot to
        // fall back to {Web,Android,iOS} and inject `wasm` into the
        // scaffold command even when the user picked only Android+iOS.
        var platforms = ProxyReader.ReadHashSet<PlatformKind>(proxy, "SelectedPlatforms");
        if (platforms.IsEmpty)
            platforms = ImmutableHashSet.Create(PlatformKind.Web, PlatformKind.Android, PlatformKind.iOS);
        var runtime = ProxyReader.ReadValue<RuntimeKind>(proxy, "SelectedRuntime") ?? RuntimeKind.Net10;
        return new ComposerSnapshot(intent, design, platforms, runtime);
    }

    private void OnCopyCommandClicked(object sender, RoutedEventArgs e)
    {
        var snap = ResolveSnapshot();
        var ctx = IntentContext.DeriveFrom(snap.Intent);
        var cmd = MarkdownGenerators.BuildScaffoldCommand(snap, ctx);
        CompositionPage.CopyToClipboard(cmd);
        ShowStatus("Scaffold command copied to clipboard.");
    }

    private void OnCopyPromptContextClicked(object sender, RoutedEventArgs e)
    {
        var snap = ResolveSnapshot();
        CompositionPage.CopyToClipboard(MarkdownGenerators.BuildPromptContext(snap));
        ShowStatus("prompt-context.md copied to clipboard.");
    }

    private void OnDownloadBundleClicked(object sender, RoutedEventArgs e)
    {
        // Dispatch to CompositionModel.DownloadBundle — opens FileSavePicker
        // for a ZIP archive containing one markdown per layer (override > generated).
        Composer.Presentation.Controls.MvuxCommandInvoker.Invoke(DataContext, "DownloadBundle");
    }

    private void ShowStatus(string message)
    {
        BundleStatusText.Text = message;
        BundleStatusText.Visibility = Visibility.Visible;
    }
}
