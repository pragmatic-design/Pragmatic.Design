using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Default <see cref="ITenantOptions{T}"/>: binds <typeparamref name="T"/> from the appsettings section
///     (fallback) overlaid by the per-tenant store cascade resolved via <see cref="IConfigurationResolver"/>.
/// </summary>
/// <remarks>
///     With an <see cref="ITenantOptionsBinder{T}" /> registered, which the generator emits for a
///     <c>[Configuration]</c> class whose properties it can bind, each property is read from the overlay, then
///     from appsettings, and set typed: no <c>ConfigurationBuilder</c> is built and no reflective <c>Bind</c> runs
///     per call (#139). Without one, the overlay and the section are composed and bound by
///     <c>ConfigurationBinder</c>, as before.
/// </remarks>
public sealed class TenantOptions<T>(
    IConfigurationResolver resolver,
    IConfiguration configuration,
    ITenantOptionsBinder<T>? binder = null) : ITenantOptions<T>
    where T : class, new()
{
    private static readonly string Section = ConfigurationSectionResolver.Resolve<T>();
    private static readonly string Prefix = Section + ":";

    public async Task<T> GetAsync(CancellationToken ct = default)
    {
        // Store cascade (tenant → environment → base) for this section.
        var resolved = await resolver.ResolveSectionAsync(Prefix, ct).ConfigureAwait(false);

        return binder is null ? BindByReflection(resolved) : BindTyped(binder, resolved);
    }

    private T BindTyped(ITenantOptionsBinder<T> typed, IReadOnlyDictionary<string, string> resolved)
    {
        // Keys compare as configuration keys do: without regard to case.
        var overlay = new Dictionary<string, string>(resolved.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in resolved)
            overlay[Relative(key)] = value;

        var section = configuration.GetSection(Section);
        var result = new T();
        typed.Bind(result, name => overlay.TryGetValue(name, out var value) ? value : section[name]);
        return result;
    }

    private T BindByReflection(IReadOnlyDictionary<string, string> resolved)
    {
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
