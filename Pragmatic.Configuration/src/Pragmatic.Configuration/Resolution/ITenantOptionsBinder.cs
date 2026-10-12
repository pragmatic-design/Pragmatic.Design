namespace Pragmatic.Configuration.Resolution;

/// <summary>
///     Binds an options class from raw configuration values, typed, without reflection. The generator emits one
///     for each <c>[Configuration]</c> class whose properties it can bind, and registers it beside the class.
/// </summary>
/// <remarks>
///     <see cref="TenantOptions{T}" /> uses it when one is registered: it reads each property from the tenant's
///     store overlay, falling back to appsettings, and builds no <c>ConfigurationBuilder</c> and runs no
///     reflective <c>Bind</c> per call (#139). A class without one keeps the reflective path.
/// </remarks>
/// <typeparam name="T">The options class.</typeparam>
public interface ITenantOptionsBinder<in T>
    where T : class
{
    /// <summary>Sets each property of <paramref name="target" /> that <paramref name="value" /> has a value for.</summary>
    /// <param name="target">A new instance, carrying the class's defaults.</param>
    /// <param name="value">
    ///     The raw value for a key relative to the class's section (the property name), or null when no source
    ///     has one, in which case the property keeps its default.
    /// </param>
    void Bind(T target, Func<string, string?> value);
}
