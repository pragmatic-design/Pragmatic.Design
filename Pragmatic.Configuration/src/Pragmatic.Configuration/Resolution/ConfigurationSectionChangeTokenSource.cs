using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Tells <see cref="IOptionsMonitor{TOptions}" /> that the options bound from a configuration section must be
///     rebuilt: its change token is the section's reload token.
/// </summary>
/// <remarks>
///     The generated <c>Add{Type}Options</c> registers it beside its own <c>Configure</c>, in place of
///     <c>OptionsBuilder.Bind</c>, which registers Microsoft's equivalent but lives in
///     <c>Microsoft.Extensions.Options.ConfigurationExtensions</c>, a package this module does not depend on (#195).
/// </remarks>
/// <typeparam name="TOptions">The options class.</typeparam>
public sealed class ConfigurationSectionChangeTokenSource<TOptions>(IConfiguration section) : IOptionsChangeTokenSource<TOptions>
{
    /// <summary>The default options name, the one <c>Configure</c> without a name configures.</summary>
    public string Name => Microsoft.Extensions.Options.Options.DefaultName;

    public IChangeToken GetChangeToken() => section.GetReloadToken();
}
