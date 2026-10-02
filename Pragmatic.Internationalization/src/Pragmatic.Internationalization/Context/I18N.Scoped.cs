using Pragmatic.Internationalization.Scopes;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Scoped-execution facade of <see cref="I18N"/>: <c>WithCulture</c> / <c>WithDataCulture</c> /
///     <c>WithScope</c> overloads that run a delegate under a temporary culture and restore on exit.
/// </summary>
public static partial class I18N
{
    /// <summary>
    ///     Executes an action within a specific UI culture context.
    /// </summary>
    public static void WithCulture(CultureCode culture, Action action)
    {
        I18NContext.WithCulture(culture, action);
    }

    /// <summary>
    ///     Executes an action within a specific UI culture context (string overload).
    /// </summary>
    public static void WithCulture(string culture, Action action)
    {
        I18NContext.WithCulture(culture, action);
    }

    /// <summary>
    ///     Executes a function within a specific UI culture context.
    /// </summary>
    public static T WithCulture<T>(CultureCode culture, Func<T> func)
    {
        return I18NContext.WithCulture(culture, func);
    }

    /// <summary>
    ///     Executes a function within a specific UI culture context (string overload).
    /// </summary>
    public static T WithCulture<T>(string culture, Func<T> func)
    {
        return I18NContext.WithCulture(culture, func);
    }

    /// <summary>
    ///     Executes an async action within a specific UI culture context.
    /// </summary>
    public static Task WithCultureAsync(CultureCode culture, Func<Task> func)
    {
        return I18NContext.WithCultureAsync(culture, func);
    }

    /// <summary>
    ///     Executes an async action within a specific UI culture context (string overload).
    /// </summary>
    public static Task WithCultureAsync(string culture, Func<Task> func)
    {
        return I18NContext.WithCultureAsync(culture, func);
    }

    /// <summary>
    ///     Executes an async function within a specific UI culture context.
    /// </summary>
    public static Task<T> WithCultureAsync<T>(CultureCode culture, Func<Task<T>> func)
    {
        return I18NContext.WithCultureAsync(culture, func);
    }

    /// <summary>
    ///     Executes an async function within a specific UI culture context (string overload).
    /// </summary>
    public static Task<T> WithCultureAsync<T>(string culture, Func<Task<T>> func)
    {
        return I18NContext.WithCultureAsync(culture, func);
    }

    /// <summary>
    ///     Executes an action within a specific Data culture context.
    /// </summary>
    public static void WithDataCulture(CultureCode culture, Action action)
    {
        I18NContext.WithDataCulture(culture, action);
    }

    /// <summary>
    ///     Executes a function within a specific Data culture context.
    /// </summary>
    public static T WithDataCulture<T>(CultureCode culture, Func<T> func)
    {
        return I18NContext.WithDataCulture(culture, func);
    }

    /// <summary>
    ///     Executes an action within a specific custom scope culture.
    /// </summary>
    public static void WithScope(string scopeName, CultureCode culture, Action action)
    {
        I18NContext.WithScope(scopeName, culture, action);
    }

    /// <summary>
    ///     Executes a function within a specific custom scope culture.
    /// </summary>
    public static T WithScope<T>(string scopeName, CultureCode culture, Func<T> func)
    {
        return I18NContext.WithScope(scopeName, culture, func);
    }

    /// <summary>
    ///     Executes an async action within a specific custom scope culture.
    /// </summary>
    public static Task WithScopeAsync(string scopeName, CultureCode culture, Func<Task> func)
    {
        return I18NContext.WithScopeAsync(scopeName, culture, func);
    }

    /// <summary>
    ///     Executes an action within a strongly-typed scope culture.
    /// </summary>
    public static void WithScope<TScope>(CultureCode culture, Action action) where TScope : ICultureScope
    {
        I18NContext.WithScope<TScope>(culture, action);
    }

    /// <summary>
    ///     Executes a function within a strongly-typed scope culture.
    /// </summary>
    public static T WithScope<TScope, T>(CultureCode culture, Func<T> func) where TScope : ICultureScope
    {
        return I18NContext.WithScope<TScope, T>(culture, func);
    }

    /// <summary>
    ///     Executes an async action within a strongly-typed scope culture.
    /// </summary>
    public static Task WithScopeAsync<TScope>(CultureCode culture, Func<Task> func) where TScope : ICultureScope
    {
        return I18NContext.WithScopeAsync<TScope>(culture, func);
    }
}
