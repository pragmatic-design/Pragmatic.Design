using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Default <see cref="ITenantOptions{T}"/>: binds <typeparamref name="T"/> from the appsettings section
///     (fallback) overlaid by the per-tenant store cascade resolved via <see cref="IConfigurationResolver"/>.
/// </summary>
public sealed class TenantOptions<T>(
    IConfigurationResolver resolver,
    IConfiguration configuration) : ITenantOptions<T>
    where T : class, new()
{
    private static readonly string Section = ConfigurationSectionResolver.Resolve<T>();
    private static readonly string Prefix = Section + ":";

    public async Task<T> GetAsync(CancellationToken ct = default)
    {
        // Store cascade (tenant → environment → base) for this section.
        var resolved = await resolver.ResolveSectionAsync(Prefix, ct).ConfigureAwait(false);

        var builder = new ConfigurationBuilder();
        // appsettings section is the fallback base...
        builder.AddConfiguration(configuration.GetSection(Section));
        // ...overlaid by the store so a per-tenant value wins.
        builder.AddInMemoryCollection(
            resolved.Select(kv => new KeyValuePair<string, string?>(Relative(kv.Key), kv.Value)));

        var result = new T();
        builder.Build().Bind(result);
        return result;
    }

    private static string Relative(string fullKey) =>
        fullKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? fullKey.Substring(Prefix.Length)
            : fullKey;
}
