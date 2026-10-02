using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

public sealed partial class I18NContext
{
    // ══════════════════════════════════════════════════════════════
    // Scoped Execution Helpers (DRY)
    // ══════════════════════════════════════════════════════════════

    private static void RunScoped(Action setup, Action restore, Action body)
    {
        try
        {
            setup();
            body();
        }
        finally
        {
            restore();
        }
    }

    private static T RunScoped<T>(Action setup, Action restore, Func<T> body)
    {
        try
        {
            setup();
            return body();
        }
        finally
        {
            restore();
        }
    }

    private static async Task RunScopedAsync(Action setup, Action restore, Func<Task> body)
    {
        try
        {
            setup();
            await body().ConfigureAwait(false);
        }
        finally
        {
            restore();
        }
    }

    private static async Task<T> RunScopedAsync<T>(Action setup, Action restore, Func<Task<T>> body)
    {
        try
        {
            setup();
            return await body().ConfigureAwait(false);
        }
        finally
        {
            restore();
        }
    }

    // ══════════════════════════════════════════════════════════════
    // WithCulture — UI culture scope
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Executes an action within a specific UI culture context.
    /// </summary>
    public static void WithCulture(CultureCode culture, Action action)
    {
        var prev = SCurrent.Value;
        var prevUI = Thread.CurrentThread.CurrentUICulture;
        var prevThread = Thread.CurrentThread.CurrentCulture;

        RunScoped(
            () => SetCulture(culture),
            () => { SCurrent.Value = prev; Thread.CurrentThread.CurrentUICulture = prevUI; Thread.CurrentThread.CurrentCulture = prevThread; },
            action);
    }

    /// <summary>
    ///     Executes an action within a specific UI culture context (string overload).
    /// </summary>
    public static void WithCulture(string culture, Action action)
    {
        WithCulture(Types.CultureCode.FromString(culture), action);
    }

    /// <summary>
    ///     Executes a function within a specific UI culture context.
    /// </summary>
    public static T WithCulture<T>(CultureCode culture, Func<T> func)
    {
        var prev = SCurrent.Value;
        var prevUI = Thread.CurrentThread.CurrentUICulture;
        var prevThread = Thread.CurrentThread.CurrentCulture;

        return RunScoped(
            () => SetCulture(culture),
            () => { SCurrent.Value = prev; Thread.CurrentThread.CurrentUICulture = prevUI; Thread.CurrentThread.CurrentCulture = prevThread; },
            func);
    }

    /// <summary>
    ///     Executes a function within a specific UI culture context (string overload).
    /// </summary>
    public static T WithCulture<T>(string culture, Func<T> func)
    {
        return WithCulture(Types.CultureCode.FromString(culture), func);
    }

    /// <summary>
    ///     Executes an async action within a specific UI culture context.
    /// </summary>
    /// <remarks>
    ///     Only <see cref="I18NContext.Current"/> (AsyncLocal) is restored on exit.
    ///     <see cref="Thread.CurrentThread"/> culture is intentionally <b>not</b> restored
    ///     after the await resumes — the continuation may run on a different thread-pool
    ///     thread than the caller, so writing the "previous" culture there would corrupt
    ///     an unrelated thread's state. Consumers that need culture-aware formatting across
    ///     async boundaries must read from <see cref="I18NContext.Current"/>, not from
    ///     <c>Thread.CurrentThread.CurrentCulture</c>.
    /// </remarks>
    public static Task WithCultureAsync(CultureCode culture, Func<Task> func)
    {
        var prev = SCurrent.Value;

        return RunScopedAsync(
            () => SetCulture(culture),
            () => { SCurrent.Value = prev; },
            func);
    }

    /// <summary>
    ///     Executes an async action within a specific UI culture context (string overload).
    /// </summary>
    public static Task WithCultureAsync(string culture, Func<Task> func)
    {
        return WithCultureAsync(Types.CultureCode.FromString(culture), func);
    }

    /// <summary>
    ///     Executes an async function within a specific UI culture context.
    /// </summary>
    /// <remarks>
    ///     See the overload without a return value for why <see cref="Thread.CurrentThread"/>
    ///     culture is intentionally not restored in the async path.
    /// </remarks>
    public static Task<T> WithCultureAsync<T>(CultureCode culture, Func<Task<T>> func)
    {
        var prev = SCurrent.Value;

        return RunScopedAsync(
            () => SetCulture(culture),
            () => { SCurrent.Value = prev; },
            func);
    }

    /// <summary>
    ///     Executes an async function within a specific UI culture context (string overload).
    /// </summary>
    public static Task<T> WithCultureAsync<T>(string culture, Func<Task<T>> func)
    {
        return WithCultureAsync(Types.CultureCode.FromString(culture), func);
    }

    // ══════════════════════════════════════════════════════════════
    // WithDataCulture — Data culture scope
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Executes an action within a specific Data culture context.
    /// </summary>
    public static void WithDataCulture(CultureCode culture, Action action)
    {
        var prev = SCurrent.Value;
        var prevThread = Thread.CurrentThread.CurrentCulture;

        RunScoped(
            () => SetDataCulture(culture),
            () => { SCurrent.Value = prev; Thread.CurrentThread.CurrentCulture = prevThread; },
            action);
    }

    /// <summary>
    ///     Executes a function within a specific Data culture context.
    /// </summary>
    public static T WithDataCulture<T>(CultureCode culture, Func<T> func)
    {
        var prev = SCurrent.Value;
        var prevThread = Thread.CurrentThread.CurrentCulture;

        return RunScoped(
            () => SetDataCulture(culture),
            () => { SCurrent.Value = prev; Thread.CurrentThread.CurrentCulture = prevThread; },
            func);
    }

    // ══════════════════════════════════════════════════════════════
    // WithScope — Custom scope
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Executes an action within a specific custom scope culture.
    /// </summary>
    public static void WithScope(string scopeName, CultureCode culture, Action action)
    {
        var prev = SCurrent.Value;
        var prevThread = Thread.CurrentThread.CurrentCulture;

        RunScoped(
            () => { SetScope(scopeName, culture); Thread.CurrentThread.CurrentCulture = culture.ToCultureInfo(); },
            () => { SCurrent.Value = prev; Thread.CurrentThread.CurrentCulture = prevThread; },
            action);
    }

    /// <summary>
    ///     Executes a function within a specific custom scope culture.
    /// </summary>
    public static T WithScope<T>(string scopeName, CultureCode culture, Func<T> func)
    {
        var prev = SCurrent.Value;
        var prevThread = Thread.CurrentThread.CurrentCulture;

        return RunScoped(
            () => { SetScope(scopeName, culture); Thread.CurrentThread.CurrentCulture = culture.ToCultureInfo(); },
            () => { SCurrent.Value = prev; Thread.CurrentThread.CurrentCulture = prevThread; },
            func);
    }

    /// <summary>
    ///     Executes an async action within a specific custom scope culture.
    /// </summary>
    /// <remarks>
    ///     Only <see cref="I18NContext.Current"/> (AsyncLocal) is restored on exit.
    ///     <see cref="Thread.CurrentThread"/> culture is intentionally <b>not</b> restored
    ///     after the await resumes — the continuation may run on a different thread-pool
    ///     thread, so writing the "previous" culture there would corrupt an unrelated
    ///     thread's state.
    /// </remarks>
    public static Task WithScopeAsync(string scopeName, CultureCode culture, Func<Task> func)
    {
        var prev = SCurrent.Value;

        return RunScopedAsync(
            () => { SetScope(scopeName, culture); Thread.CurrentThread.CurrentCulture = culture.ToCultureInfo(); },
            () => { SCurrent.Value = prev; },
            func);
    }

    // ══════════════════════════════════════════════════════════════
    // Strongly-Typed Scope Execution
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    ///     Executes an action within a strongly-typed scope culture.
    /// </summary>
    public static void WithScope<TScope>(CultureCode culture, Action action) where TScope : ICultureScope
    {
        WithScope(TScope.Name, culture, action);
    }

    /// <summary>
    ///     Executes a function within a strongly-typed scope culture.
    /// </summary>
    public static T WithScope<TScope, T>(CultureCode culture, Func<T> func) where TScope : ICultureScope
    {
        return WithScope<T>(TScope.Name, culture, func);
    }

    /// <summary>
    ///     Executes an async action within a strongly-typed scope culture.
    /// </summary>
    public static Task WithScopeAsync<TScope>(CultureCode culture, Func<Task> func) where TScope : ICultureScope
    {
        return WithScopeAsync(TScope.Name, culture, func);
    }
}
