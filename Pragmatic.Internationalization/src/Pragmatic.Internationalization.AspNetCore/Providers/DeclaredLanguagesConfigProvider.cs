using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Providers;

/// <summary>
///     The languages the modules' translations are written in, below everything the application configures.
/// </summary>
/// <remarks>
///     <para>
///         Registered by the generated host from the modules' translation metadata: the cultures of their
///         <c>translations/{culture}.json</c> files are the supported ones, and the culture those files are
///         written from — <c>[TranslationKeys(DefaultCulture = …)]</c>, the one missing keys are checked
///         against — is the default, when every module agrees on it and has it.
///     </para>
///     <para>
///         Lowest priority: <c>UseI18N</c>, the <c>I18N</c> configuration section and every other provider
///         override it. It only answers what nobody else did — and when it cannot answer the default
///         either, the host refuses to start instead of failing every request.
///     </para>
/// </remarks>
public sealed class DeclaredLanguagesConfigProvider : II18NConfigProvider
{
    private readonly I18NConfig _config;

    /// <summary>Creates the provider from what the modules declare.</summary>
    /// <param name="defaultCulture">The culture the translations are written from, or <c>null</c> when it is not decidable.</param>
    /// <param name="cultures">The cultures of the translation files.</param>
    public DeclaredLanguagesConfigProvider(string? defaultCulture, IReadOnlyList<string> cultures)
    {
        Ensure.Ensure.ThrowIfNull(cultures);

        var @default = defaultCulture is null ? (CultureCode?)null : CultureCode.FromString(defaultCulture);
        _config = new I18NConfig
        {
            DefaultUICulture = @default,
            DefaultDataCulture = @default,
            SupportedCultures = cultures.Count == 0 ? null : [.. cultures.Select(CultureCode.FromString)]
        };
    }

    /// <summary>Below <see cref="SystemConfigProvider" /> (0): whatever the application configures wins.</summary>
    public int Priority => -100;

    /// <inheritdoc />
    public I18NConfig GetConfiguration() => _config;
}
