using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Windows.Input;

namespace Composer.Presentation.Controls;

/// <summary>
/// Shared command-dispatch helper for the MVUX bindable proxy. The MVUX source
/// generator exposes async methods on the model as ICommand properties on the
/// generated ViewModel — sometimes under the bare method name, sometimes with
/// a "Command" suffix depending on shape. This helper resolves either form
/// once per (type, name) pair, caches the resolved PropertyInfo, then
/// dispatches.
///
/// Used by code-behind handlers that can't go through XAML binding (e.g., the
/// Drop event on the screenshot panel — drag/drop event args don't bind).
/// </summary>
internal static class MvuxCommandInvoker
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _cache = new();

    public static void Invoke(object? dataContext, string methodName, object? parameter = null)
    {
        if (dataContext is null) return;

        var prop = _cache.GetOrAdd((dataContext.GetType(), methodName),
            key => key.Item1.GetProperty(key.Item2)
                   ?? key.Item1.GetProperty(key.Item2 + "Command"));

        if (prop?.GetValue(dataContext) is ICommand cmd)
            cmd.Execute(parameter);
    }
}
