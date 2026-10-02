using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Testing;

/// <summary>
///     Static helper methods for testing with isolated I18N contexts.
/// </summary>
/// <example>
/// <code>
/// // Execute code with a specific culture
/// TestI18N.WithCulture(CultureCode.Italian, () =>
/// {
///     var formatted = price.Format();
///     // assertions...
/// });
///
/// // With return value
/// var result = TestI18N.WithCulture(CultureCode.German, () =>
/// {
///     return ComputeLocalizedValue();
/// });
/// </code>
/// </example>
public static class TestI18N
{
    /// <summary>
    ///     Executes an action with the specified culture, then restores the previous context.
    /// </summary>
    /// <param name="culture">The culture to use during execution.</param>
    /// <param name="action">The action to execute.</param>
    public static void WithCulture(CultureCode culture, Action action)
    {
        using var scope = new TestI18NScope(culture);
        action();
    }

    /// <summary>
    ///     Executes a function with the specified culture, then restores the previous context.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="culture">The culture to use during execution.</param>
    /// <param name="func">The function to execute.</param>
    /// <returns>The result of the function.</returns>
    public static T WithCulture<T>(CultureCode culture, Func<T> func)
    {
        using var scope = new TestI18NScope(culture);
        return func();
    }

    /// <summary>
    ///     Executes an async action with the specified culture.
    /// </summary>
    /// <param name="culture">The culture to use during execution.</param>
    /// <param name="func">The async action to execute.</param>
    public static async Task WithCultureAsync(CultureCode culture, Func<Task> func)
    {
        using var scope = new TestI18NScope(culture);
        await func().ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes an async function with the specified culture.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="culture">The culture to use during execution.</param>
    /// <param name="func">The async function to execute.</param>
    /// <returns>The result of the function.</returns>
    public static async Task<T> WithCultureAsync<T>(CultureCode culture, Func<Task<T>> func)
    {
        using var scope = new TestI18NScope(culture);
        return await func().ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes an action with separate UI and Data cultures.
    /// </summary>
    /// <param name="uiCulture">The UI culture to use.</param>
    /// <param name="dataCulture">The data culture to use.</param>
    /// <param name="action">The action to execute.</param>
    public static void WithCultures(CultureCode uiCulture, CultureCode dataCulture, Action action)
    {
        using var scope = new TestI18NScope(uiCulture, dataCulture);
        action();
    }

    /// <summary>
    ///     Executes a function with separate UI and Data cultures.
    /// </summary>
    /// <typeparam name="T">The return type.</typeparam>
    /// <param name="uiCulture">The UI culture to use.</param>
    /// <param name="dataCulture">The data culture to use.</param>
    /// <param name="func">The function to execute.</param>
    /// <returns>The result of the function.</returns>
    public static T WithCultures<T>(CultureCode uiCulture, CultureCode dataCulture, Func<T> func)
    {
        using var scope = new TestI18NScope(uiCulture, dataCulture);
        return func();
    }
}
