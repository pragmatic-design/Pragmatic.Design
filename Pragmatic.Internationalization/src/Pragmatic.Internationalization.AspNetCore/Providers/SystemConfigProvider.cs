using Microsoft.Extensions.Options;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Providers;

/// <summary>
///     Configuration provider that reads from <see cref="IOptions{TOptions}"/>.
/// </summary>
/// <remarks>
///     <para>
///         This provider has the lowest priority (0) and serves as the fallback
///         when no other provider sets a value.
///     </para>
///     <para>
///         Configuration is typically set via <c>AddPragmaticInternationalization(options => ...)</c>
///         or from appsettings.json.
///     </para>
/// </remarks>
public sealed class SystemConfigProvider : II18NConfigProvider
{
    private readonly IOptions<I18NOptions> _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemConfigProvider"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    public SystemConfigProvider(IOptions<I18NOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public I18NConfig? GetConfiguration()
    {
        var opts = _options.Value;

        // If no options are configured, return null to defer to other providers
        // This supports the "fully dynamic" scenario where all config comes from DB.
        // CustomScopes alone (e.g. registered via AddScope) still counts as configuration.
        if (opts.DefaultUICulture is null &&
            opts.DefaultDataCulture is null &&
            opts.SupportedCultures is null &&
            (opts.CustomScopes is null || opts.CustomScopes.Count == 0))
        {
            return null;
        }

        return new I18NConfig
        {
            DefaultUICulture = opts.DefaultUICulture,
            DefaultDataCulture = opts.DefaultDataCulture ?? opts.DefaultUICulture,
            SyncScopes = opts.SyncScopes,
            SupportedCultures = opts.SupportedCultures,
            CustomScopes = opts.CustomScopes is null
                ? null
                : new Dictionary<string, CultureCode>(opts.CustomScopes)
        };
    }
}
