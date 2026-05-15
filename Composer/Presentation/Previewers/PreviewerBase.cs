using System.ComponentModel;
using Microsoft.UI.Xaml;
using Composer.Models;
using Composer.Presentation.Controls;

namespace Composer.Presentation.Previewers;

// Shared subscription logic for every previewer. Each previewer wraps its
// content in a FrameworkElement named "Inner" (Border, Grid, StackPanel).
// On parent DataContext (the MVUX-generated proxy) changes, we re-derive
// IntentContext and set Inner.DataContext to it — local XAML bindings then
// resolve against IntentContext directly. Avoids the MVUX proxy's binding
// limitations around primitive feeds (composer-delta-brief.md §4).
internal static class PreviewerHelper
{
    public static void Wire(FrameworkElement host, FrameworkElement inner)
    {
        INotifyPropertyChanged? subscribedModel = null;

        void Refresh()
        {
            var intent = ReadIntent(subscribedModel);
            var ctx = IntentContext.DeriveFrom(intent);
            inner.DataContext = ctx;
            System.Diagnostics.Debug.WriteLine($"[Previewer] Inner.DataContext set; FlowLabel={ctx.FlowLabel} EntityTitle={ctx.EntityTitle}");
        }

        // Filter the chatter to changes that actually affect the previewer.
        // After the Intent decomposition, the proxy fires PropertyChanged per
        // field — never for the composed "Intent" wrapper — so the previous
        // "Intent" or null filter would never match the per-field events.
        void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "AppType" or "PrimaryUser" or "Workflow" or "Platforms" or null)
                host.DispatcherQueue?.TryEnqueue(Refresh);
        }

        void Detach()
        {
            if (subscribedModel is null) return;
            subscribedModel.PropertyChanged -= OnModelChanged;
            subscribedModel = null;
        }

        void Attach()
        {
            Detach();
            subscribedModel = host.DataContext as INotifyPropertyChanged;
            if (subscribedModel is not null)
                subscribedModel.PropertyChanged += OnModelChanged;
            Refresh();
        }

        host.DataContextChanged += (_, _) => Attach();
        host.Unloaded += (_, _) => Detach();

        if (host.IsLoaded) Attach();
        else host.Loaded += (_, _) => Attach();
    }

    private static Intent ReadIntent(INotifyPropertyChanged? model)
        => ProxyReader.ReadIntent(model);
}
