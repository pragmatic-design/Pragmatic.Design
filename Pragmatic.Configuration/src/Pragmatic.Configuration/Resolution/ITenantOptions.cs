namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Resolves a strongly-typed options object <typeparamref name="T"/> for the <b>current tenant</b>, layering
///     the per-tenant configuration store over the application defaults (appsettings). This is the typed facade
///     over <see cref="ConfigurationResolver"/>: the store cascade (tenant → environment → base) wins over the
///     appsettings fallback, so a validator can read tenant-scoped settings without touching global
///     <c>IConfiguration</c> directly (#5).
/// </summary>
/// <typeparam name="T">The options POCO. Its section is taken from <c>[Configuration(SectionPath = …)]</c> or inferred from the type name.</typeparam>
public interface ITenantOptions<T>
    where T : class, new()
{
    /// <summary>Binds and returns the options for the current tenant.</summary>
    Task<T> GetAsync(CancellationToken ct = default);
}
