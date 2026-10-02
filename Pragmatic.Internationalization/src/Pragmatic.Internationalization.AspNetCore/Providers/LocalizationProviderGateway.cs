using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Providers;

/// <summary>
///     What an application gets when it asks the container for an <see cref="ILocalizationProvider" />:
///     every registered source, through the composite.
/// </summary>
/// <remarks>
///     <para>
///         Each translation source is registered under <see cref="ILocalizationProvider" />, and the
///         last registration is what <c>GetRequiredService</c> returns — so without this type the obvious
///         line to write reads one source out of however many the application has configured. It does
///         not fail: a key the chosen source does not define comes back unchanged, which is exactly what
///         a missing translation looks like, so the wrong answer is indistinguishable from a legitimate
///         one.
///     </para>
///     <para>
///         This is registered last under the same interface, and delegates everything to
///         <see cref="CompositeLocalizationProvider" />.
///     </para>
///     <para>
///         ⚠️ <b>The composite is resolved on use, not on construction, and that is the whole trick.</b>
///         The composite collects its sources from <c>GetServices&lt;ILocalizationProvider&gt;()</c>,
///         which necessarily enumerates this registration too. Resolving the composite in this
///         constructor would therefore ask for the composite while the composite is being built, and
///         the built-in container cannot see inside a factory to call that a cycle: it would recurse
///         until the stack ran out. Holding the provider and resolving on first lookup is what breaks
///         it — together with the filter in <c>I18NBuilder.EnsureCompositeRegistered</c>, which keeps
///         the composite from collecting this type as one of its own sources.
///     </para>
/// </remarks>
internal sealed class LocalizationProviderGateway(IServiceProvider services) : ILocalizationProvider
{
    private CompositeLocalizationProvider Composite
        => field ??= services.GetRequiredService<CompositeLocalizationProvider>();

    /// <inheritdoc />
    public string? GetString(string key, string culture) => Composite.GetString(key, culture);

    /// <inheritdoc />
    public PluralString? GetPlural(string key, string culture) => Composite.GetPlural(key, culture);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetAll(string culture) => Composite.GetAll(culture);

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PluralString> GetAllPlurals(string culture)
        => Composite.GetAllPlurals(culture);

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedCultures => Composite.SupportedCultures;

    /// <inheritdoc />
    public int Priority => Composite.Priority;
}
