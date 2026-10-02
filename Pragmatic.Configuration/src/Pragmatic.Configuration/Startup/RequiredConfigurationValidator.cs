using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Configuration.Resolution;

namespace Pragmatic.Configuration.Startup;

/// <summary>
///     Fail-fast startup check: resolves every required key through the cascade and aborts host start
///     (throws from <see cref="StartAsync" />) listing any that are unset. Resolution runs in a fresh scope so
///     the scoped <see cref="IConfigurationResolver" /> can be used before any request scope exists.
/// </summary>
internal sealed class RequiredConfigurationValidator(
    RequiredConfigurationKeys required, IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (required.Keys.Count == 0)
            return;

        using var scope = scopeFactory.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IConfigurationResolver>();

        var missing = new List<string>();
        foreach (var key in required.Keys.Distinct(StringComparer.Ordinal))
            if (await resolver.ResolveAsync(key, cancellationToken).ConfigureAwait(false) is null)
                missing.Add(key);

        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Required configuration is missing (no value resolved for these keys): " +
                string.Join(", ", missing) + ".");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
