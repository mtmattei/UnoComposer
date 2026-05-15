using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using Composer.Models;

namespace Composer.Presentation.Controls;

/// <summary>
/// Shared helper for reading state values from the MVUX bindable proxy via
/// reflection. The proxy wraps every <c>IState&lt;T&gt;</c> in a
/// <c>Bindable{T}ViewModel</c> whose <c>.Value</c> property returns the
/// underlying record / value type — a direct <c>as T</c> cast returns null,
/// so this helper unwraps via <c>.Value</c> as a fallback. For collection
/// state (<c>IState&lt;ImmutableHashSet&lt;T&gt;&gt;</c> etc.) the wrapper
/// sometimes exposes the inner shape as an interface that the direct cast
/// to the concrete <c>ImmutableHashSet&lt;T&gt;</c> rejects, so the helper
/// also iterates as <c>IEnumerable</c> as a last resort.
///
/// Caches <c>PropertyInfo</c> lookups keyed by (type, property name) so the
/// hot poll-tick paths in layer views don't re-resolve on every refresh.
///
/// Replaces ~11 duplicate <c>ReadIntent</c> / <c>ReadDesignTokens</c>
/// methods scattered across layer views and previewers.
/// </summary>
internal static class ProxyReader
{
    // (declaringType, propertyName) → PropertyInfo? — single shared cache.
    // PropertyInfo lookups via Type.GetProperty(string) walk the property
    // table on every call; caching turns that into an O(1) dictionary read.
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _propertyCache = new();

    /// <summary>Reads <paramref name="propertyName"/> off <paramref name="source"/>
    /// and casts to <typeparamref name="T"/>, falling back to the wrapper's
    /// <c>.Value</c> if the direct cast fails. Returns null if neither path
    /// produces a <typeparamref name="T"/>.</summary>
    public static T? Read<T>(object? source, string propertyName) where T : class
    {
        if (source is null) return null;
        var raw = GetProperty(source.GetType(), propertyName)?.GetValue(source);
        if (raw is T direct) return direct;
        var unwrapped = GetProperty(raw?.GetType(), "Value")?.GetValue(raw);
        return unwrapped as T;
    }

    /// <summary>Reads a value-typed property (struct / enum / primitive)
    /// from the proxy, falling back to the wrapper's <c>.Value</c>.
    /// Returns null when neither path produces a <typeparamref name="T"/>.</summary>
    public static T? ReadValue<T>(object? source, string propertyName) where T : struct
    {
        if (source is null) return null;
        var raw = GetProperty(source.GetType(), propertyName)?.GetValue(source);
        if (raw is T direct) return direct;
        var unwrapped = GetProperty(raw?.GetType(), "Value")?.GetValue(raw);
        return unwrapped is T wrapped ? wrapped : null;
    }

    /// <summary>Reads an <see cref="ImmutableHashSet{TItem}"/>-shaped collection
    /// off the proxy, robust to the proxy's varied collection exposures
    /// (raw set, wrapped <c>.Value</c>, or a bare <see cref="IEnumerable"/>).
    /// Returns an empty set when nothing readable is found.</summary>
    public static ImmutableHashSet<TItem> ReadHashSet<TItem>(object? source, string propertyName)
    {
        if (source is null) return ImmutableHashSet<TItem>.Empty;
        var raw = GetProperty(source.GetType(), propertyName)?.GetValue(source);
        if (raw is ImmutableHashSet<TItem> direct) return direct;

        var unwrapped = GetProperty(raw?.GetType(), "Value")?.GetValue(raw);
        if (unwrapped is ImmutableHashSet<TItem> wrapped) return wrapped;

        // Last resort — the proxy may expose the set as IEnumerable or an
        // interface type. Iterate and rebuild.
        var seq = (unwrapped as IEnumerable) ?? (raw as IEnumerable);
        if (seq is null) return ImmutableHashSet<TItem>.Empty;
        var builder = ImmutableHashSet.CreateBuilder<TItem>();
        foreach (var item in seq)
            if (item is TItem typed) builder.Add(typed);
        return builder.ToImmutable();
    }

    /// <summary>Reads an <see cref="ImmutableArray{TItem}"/>-shaped collection
    /// off the proxy. Used for small attachment lists where the UI needs an
    /// immediate status read rather than a generated feed projection.</summary>
    public static ImmutableArray<TItem> ReadArray<TItem>(object? source, string propertyName)
    {
        if (source is null) return ImmutableArray<TItem>.Empty;
        var raw = GetProperty(source.GetType(), propertyName)?.GetValue(source);
        if (raw is ImmutableArray<TItem> direct) return direct;

        var unwrapped = GetProperty(raw?.GetType(), "Value")?.GetValue(raw);
        if (unwrapped is ImmutableArray<TItem> wrapped) return wrapped;

        var seq = (unwrapped as IEnumerable) ?? (raw as IEnumerable);
        if (seq is null) return ImmutableArray<TItem>.Empty;
        var builder = ImmutableArray.CreateBuilder<TItem>();
        foreach (var item in seq)
            if (item is TItem typed) builder.Add(typed);
        return builder.ToImmutable();
    }

    /// <summary>Returns true when <paramref name="source"/> has an
    /// <c>AsyncCommand</c>-shaped property named <paramref name="commandName"/>
    /// whose <c>IsExecuting</c> flag is true.</summary>
    public static bool IsCommandExecuting(object? source, string commandName)
    {
        if (source is null) return false;
        var cmd = GetProperty(source.GetType(), commandName)?.GetValue(source);
        return GetProperty(cmd?.GetType(), "IsExecuting")?.GetValue(cmd) is bool b && b;
    }

    /// <summary>Assembles an <see cref="Intent"/> from the four per-field
    /// <c>IState&lt;string&gt;</c> properties on the bindable proxy
    /// (<c>AppType</c>, <c>PrimaryUser</c>, <c>Workflow</c>, <c>Platforms</c>).
    /// Reading per-field bypasses the composed <c>IFeed&lt;Intent&gt;</c>
    /// propagation lag — per-field state wrappers update synchronously when
    /// their <c>UpdateAsync</c> fires <c>PropertyChanged</c>, but the
    /// composed feed's wrapper re-derives asynchronously through
    /// <c>Feed.Combine</c>, so reflection reads of <c>Intent.Value</c> can
    /// race the source-of-truth state changes.</summary>
    public static Intent ReadIntent(object? source)
    {
        if (source is null) return Intent.Example;
        var appType     = Read<string>(source, "AppType")     ?? string.Empty;
        var primaryUser = Read<string>(source, "PrimaryUser") ?? string.Empty;
        var workflow    = Read<string>(source, "Workflow")    ?? string.Empty;
        var platforms   = Read<string>(source, "Platforms")   ?? string.Empty;
        return new Intent(appType, primaryUser, workflow, platforms);
    }

    /// <summary>Assembles a <see cref="DesignTokens"/> from the nine per-field
    /// <c>IState&lt;string&gt;</c> properties on the bindable proxy. Same
    /// rationale as <see cref="ReadIntent"/>: per-field reads bypass the
    /// composed <c>IFeed&lt;DesignTokens&gt;</c> propagation lag.</summary>
    public static DesignTokens ReadDesignTokens(object? source)
    {
        if (source is null) return DesignTokens.Default;
        var surface  = Read<string>(source, "DesignSurface")  ?? DesignTokens.Default.Surface;
        var action   = Read<string>(source, "DesignAction")   ?? DesignTokens.Default.Action;
        var info     = Read<string>(source, "DesignInfo")     ?? DesignTokens.Default.Info;
        var success  = Read<string>(source, "DesignSuccess")  ?? DesignTokens.Default.Success;
        var warn     = Read<string>(source, "DesignWarn")     ?? DesignTokens.Default.Warn;
        var panel    = Read<string>(source, "DesignPanel")    ?? DesignTokens.Default.Panel;
        var tag      = Read<string>(source, "DesignTag")      ?? DesignTokens.Default.Tag;
        var locked   = Read<string>(source, "DesignLocked")   ?? DesignTokens.Default.Locked;
        var bodyFont = Read<string>(source, "DesignBodyFont") ?? DesignTokens.Default.BodyFont;
        return new DesignTokens(surface, action, info, success, warn, panel, tag, locked, bodyFont);
    }

    /// <summary>Subscribes <paramref name="handler"/> to
    /// <see cref="INotifyPropertyChanged.PropertyChanged"/> on
    /// <paramref name="source"/> when it implements INPC. Returns the
    /// subscribed source so the caller can unsubscribe on detach.</summary>
    public static INotifyPropertyChanged? AttachPropertyChanged(object? source, PropertyChangedEventHandler handler)
    {
        if (source is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += handler;
            return inpc;
        }
        return null;
    }

    private static PropertyInfo? GetProperty(Type? type, string name)
    {
        if (type is null) return null;
        return _propertyCache.GetOrAdd((type, name), key => key.Item1.GetProperty(key.Item2));
    }
}
