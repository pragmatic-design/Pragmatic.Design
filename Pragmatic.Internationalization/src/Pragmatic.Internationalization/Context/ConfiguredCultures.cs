using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Context;

/// <summary>
///     Answers <see cref="IConfiguredCultures" /> from the merged configuration.
/// </summary>
/// <remarks>
///     A wrapper and nothing more: the merging, the priorities and the validation stay in
///     <see cref="I18NConfigResolver" />, and what leaves this type is the one question a module asks.
///     Public because <c>UseI18N</c> — which registers it — is in the AspNetCore package beside this
///     one, and this assembly grants it no internals access.
/// </remarks>
public sealed class ConfiguredCultures(I18NConfigResolver resolver) : IConfiguredCultures
{
    /// <inheritdoc />
    public CultureCode Default => resolver.Resolve().DefaultUICulture!.Value;

    /// <inheritdoc />
    public IReadOnlyList<CultureCode> Supported => resolver.Resolve().SupportedCultures ?? [];
}
