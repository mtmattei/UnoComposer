using System.ComponentModel;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Layers;

public sealed partial class ArchitectureLayerView : UserControl
{
    private INotifyPropertyChanged? _bindable;

    public ArchitectureLayerView()
    {
        this.InitializeComponent();
        this.DataContextChanged += OnDataContextChanged;
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        // Re-refresh whenever this layer flips to Visible — see UxLayerView.
        this.RegisterPropertyChangedCallback(VisibilityProperty, OnVisibilityChanged);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_bindable is null && this.DataContext is INotifyPropertyChanged inpc)
        {
            _bindable = inpc;
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        }
        if (Visibility == Visibility.Visible) Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachBindable();

    private void OnVisibilityChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (Visibility == Visibility.Visible) Refresh();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        DetachBindable();
        _bindable = args.NewValue as INotifyPropertyChanged;
        if (_bindable is not null)
            _bindable.PropertyChanged += OnBindablePropertyChanged;
        if (Visibility == Visibility.Visible) Refresh();
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
            if (Visibility == Visibility.Visible) Refresh();
        });
    }

    private void Refresh()
    {
        var intent = ReadIntent() ?? Intent.Example;
        var ctx = IntentContext.DeriveFrom(intent);
        var name = ctx.AppName;
        var screens = ctx.ScreenFlow;

        RecapText.Text = $"↳ Five-screen {ctx.EntityNoun} flow with confirmation as terminal state.";

        WhyThisMattersText.Text = ctx.ArchitectureRationale;
        DataLayerText.Text = BuildDataLayerText(ctx);
        NavigationTopologyText.Text = BuildNavigationTopologyText(ctx);
        SolutionLayoutText.Text = BuildSolutionLayoutText(ctx);
        UnoFeaturesText.Text = $"<UnoFeatures>{BuildUnoFeatures(ctx)}</UnoFeatures>";

        // Solution tree — restructured per intent. Models include the
        // derived entity records (Meal.cs, User.cs, Schedule.cs for a
        // calorie tracker; Job.cs, Technician.cs, Schedule.cs for
        // field-service). Pages mirror the 5 screens from the vibe-aware
        // ScreenFlow so the tree reflects "what will get built".
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{name}/");
        sb.AppendLine($"├── {name}/");
        sb.AppendLine($"│   ├── Models/");
        sb.AppendLine($"│   │   ├── {ctx.EntityTitle}.cs");
        sb.AppendLine($"│   │   ├── {ctx.UserSingularTitle}.cs");
        sb.AppendLine($"│   │   └── Schedule.cs");
        sb.AppendLine($"│   ├── Presentation/");
        sb.AppendLine($"│   │   ├── Pages/");
        for (int i = 0; i < screens.Length; i++)
        {
            var glyph = i == screens.Length - 1 ? "│   │   │   └─" : "│   │   │   ├─";
            var pageName = ToPageFilename(screens[i].Name);
            sb.AppendLine($"{glyph} {pageName}");
        }
        sb.AppendLine($"│   │   └── ViewModels/");
        sb.AppendLine($"│   ├── Services/");
        sb.AppendLine($"│   │   └── {ctx.EntityTitle}Service.cs");
        sb.AppendLine($"│   ├── Themes/");
        sb.AppendLine($"│   │   └── ColorPaletteOverride.xaml");
        sb.Append($"│   └── App.xaml");
        SolutionTreeText.Text = sb.ToString();
    }

    private static string BuildDataLayerText(IntentContext ctx)
        => ctx.IsOfflineFirst
            ? $"{ctx.EntityTitle} repository uses MVUX feeds over local Storage, with queued sync and conflict-safe service boundaries."
            : $"{ctx.EntityTitle} repository uses MVUX feeds over DI services, typed HTTP clients, and local cache for responsive reads.";

    private static string BuildNavigationTopologyText(IntentContext ctx)
        => $"App shell routes the five-screen {ctx.FlowName} through Uno.Extensions regions: list, create/edit, detail/review, sync/status, confirmation.";

    private static string BuildSolutionLayoutText(IntentContext ctx)
        => $"{ctx.AppName}/ contains Presentation/Pages, Presentation/ViewModels, Models, Services, Storage, Themes, and App.xaml with RouteMap registration.";

    private static string BuildUnoFeatures(IntentContext ctx)
    {
        var features = new[] { "Material", "Toolkit", "Mvux", "Navigation", "Storage", "Logging", "Configuration" };
        return ctx.IsOfflineFirst ? string.Join(";", features) : string.Join(";", features.Concat(new[] { "Http" }));
    }

    private static string ToPageFilename(string screenName)
    {
        // "Meal detail" → "MealDetailPage.xaml"; "Add meal" → "AddMealPage.xaml".
        if (string.IsNullOrWhiteSpace(screenName)) return "Page.xaml";
        var parts = screenName.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var pascal = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..].ToLowerInvariant()));
        return $"{pascal}Page.xaml";
    }

    private Intent ReadIntent() => ProxyReader.ReadIntent(_bindable);
}
