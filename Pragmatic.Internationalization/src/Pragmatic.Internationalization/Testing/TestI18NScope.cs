using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Testing;

/// <summary>
///     Provides an isolated I18N context scope for unit tests.
///     The context is automatically restored when the scope is disposed.
/// </summary>
/// <remarks>
///     <para>
///         Use this class in unit tests to ensure test isolation. Each test can run
///         with its own culture settings without affecting other tests.
///     </para>
///     <para>
///         The scope captures the current context before modification and restores it
///         when disposed, making it safe to use with nested scopes.
///     </para>
/// </remarks>
/// <example>
/// <code>
/// [Fact]
/// public void PriceFormatting_Italian_UsesComma()
/// {
///     using var _ = new TestI18NScope(CultureCode.Italian);
///
///     var price = Money.From(1234.56m, CurrencyCode.EUR);
///     price.Format().Should().Contain(",");
/// }
/// </code>
/// </example>
public sealed class TestI18NScope : IDisposable
{
    private readonly I18NContextSnapshot _previous;
    private bool _disposed;

    /// <summary>
    ///     Creates a new test scope with the specified culture.
    /// </summary>
    /// <param name="culture">The culture to use in this scope.</param>
    public TestI18NScope(CultureCode culture)
    {
        _previous = I18NContext.Capture();
        I18NContext.SetCulture(culture);
    }

    /// <summary>
    ///     Creates a new test scope with full configuration.
    /// </summary>
    /// <param name="uiCulture">The UI culture to use.</param>
    /// <param name="dataCulture">The data culture to use.</param>
    public TestI18NScope(CultureCode uiCulture, CultureCode dataCulture)
    {
        _previous = I18NContext.Capture();

        var config = new I18NConfig
        {
            DefaultUICulture = uiCulture,
            DefaultDataCulture = dataCulture,
            SyncScopes = false
        };
        I18NContext.SetFromConfig(config);
    }

    /// <summary>
    ///     Restores the previous context.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        I18NContext.Restore(_previous);
    }
}
