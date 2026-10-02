namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Specifies the lifetime of a service in the dependency injection container.
/// </summary>
/// <remarks>
///     <para>
///         This enum mirrors <c>Microsoft.Extensions.DependencyInjection.ServiceLifetime</c>
///         to avoid requiring consumers to reference that namespace for attribute usage.
///     </para>
///     <para>
///         ⚠️ It is <b>not</b> called <c>ServiceLifetime</c>, and deliberately so: the Web SDK
///         imports <c>Microsoft.Extensions.DependencyInjection</c> implicitly, so in any web project that also
///         imports <c>Pragmatic.Composition.Attributes</c> — where <c>[Service]</c>, <c>[Module]</c> and
///         <c>[Include]</c> live — every mention of that name was CS0104. The namespace could not be an
///         implicit using because of this one name, and an application wrote <c>using</c> by hand or spelled
///         the value out in full.
///     </para>
/// </remarks>
public enum Lifetime
{
    /// <summary>
    ///     A single instance is created and shared across all requests.
    /// </summary>
    Singleton = 0,

    /// <summary>
    ///     A new instance is created for each scope (e.g., HTTP request).
    ///     This is the default and recommended lifetime for most services.
    /// </summary>
    Scoped = 1,

    /// <summary>
    ///     A new instance is created every time the service is requested.
    /// </summary>
    Transient = 2
}
