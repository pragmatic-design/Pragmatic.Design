using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.Context;

namespace Pragmatic.Internationalization.AspNetCore.Extensions;

/// <summary>
///     Refuses to start a host whose culture configuration can never resolve, instead of letting every
///     request fail with the same <see cref="I18NConfigurationException" />.
/// </summary>
/// <remarks>
///     <para>
///         The missing default is deliberately an error, not a silent fallback to a culture nobody chose.
///         What changes is when it surfaces: with only the static sources — the application's options and
///         the modules' declared languages — the answer is the same for every request, so it is known here.
///     </para>
///     <para>
///         ⚠️ Any other provider may answer per request — from a tenant, a user, a database — and cannot be
///         asked outside one: then the check is left to the request, as before.
///     </para>
/// </remarks>
internal static class I18NStartupCheck
{
    /// <summary>Resolves the configuration once, when only static sources contribute to it.</summary>
    /// <exception cref="I18NConfigurationException">No default UI culture can come from anywhere.</exception>
    public static void Verify(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var providers = scope.ServiceProvider.GetServices<II18NConfigProvider>().ToList();

        if (providers.Any(provider => provider is not SystemConfigProvider and not DeclaredLanguagesConfigProvider))
            return;

        new I18NConfigResolver(providers).Resolve();
    }
}
