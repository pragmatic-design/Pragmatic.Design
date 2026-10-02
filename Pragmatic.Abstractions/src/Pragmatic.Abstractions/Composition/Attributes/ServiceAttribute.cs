namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a class for automatic dependency injection registration.
///     The source generator will create registration code at compile-time.
/// </summary>
/// <remarks>
///     <para>
///         By default, services are registered with <see cref="Lifetime.Scoped" /> lifetime
///         and as the first implemented interface.
///     </para>
///     <para>
///         Use <see cref="ServiceAttribute{TInterface}" /> to explicitly specify the interface,
///         or <see cref="AsSelf" /> to register as the concrete type.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the service lifetime. Default is <see cref="Lifetime.Scoped" />.
    /// </summary>
    public Lifetime Lifetime { get; set; } = Lifetime.Scoped;

    /// <summary>
    ///     Gets or sets the service type to register as.
    ///     If not specified, the first implemented interface is used.
    ///     Prefer using <see cref="ServiceAttribute{TInterface}" /> for type-safety.
    ///     <para>
    ///         Do not set both <see cref="As"/> and <see cref="AsSelf"/> simultaneously;
    ///         the source generator treats this as a configuration error: <see cref="As"/>
    ///         takes precedence and <see cref="AsSelf"/> is ignored when both are set.
    ///         Use one or the other for clarity.
    ///     </para>
    /// </summary>
    public Type? As { get; set; }

    /// <summary>
    ///     Gets or sets whether to register as the concrete type (self).
    ///     When true, the service is registered as itself rather than an interface.
    ///     Mutually exclusive with <see cref="As"/>; see <see cref="As"/> for precedence rules.
    /// </summary>
    public bool AsSelf { get; set; }

    /// <summary>
    ///     Gets or sets the key for keyed service registration (.NET 8+).
    ///     When specified, the service is registered as a keyed service.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    ///     Registers this class as <b>one implementation among several</b> of the service — an
    ///     <c>IPermissionProvider</c>, an <c>IQueryFilter</c> — through <c>TryAddEnumerable</c>. Without it the
    ///     registration is a <c>TryAdd</c>, which keeps whichever implementation registered first and drops
    ///     this one in silence. Not combined with <see cref="Key" /> or with <c>[Inject]</c> members, which
    ///     register through a factory.
    /// </summary>
    public bool Multiple { get; set; }
}

/// <summary>
///     Marks a class for automatic dependency injection registration as a specific interface.
///     Use this when a class implements multiple interfaces and you need to specify which one to register.
/// </summary>
/// <typeparam name="TInterface">The interface type to register this service as.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceAttribute<TInterface> : Attribute
    where TInterface : class
{
    /// <summary>
    ///     Gets or sets the service lifetime. Default is <see cref="Lifetime.Scoped" />.
    /// </summary>
    public Lifetime Lifetime { get; set; } = Lifetime.Scoped;

    /// <summary>
    ///     Gets or sets the key for keyed service registration (.NET 8+).
    ///     When specified, the service is registered as a keyed service.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    ///     Registers this class as <b>one implementation among several</b> of the service — an
    ///     <c>IPermissionProvider</c>, an <c>IQueryFilter</c> — through <c>TryAddEnumerable</c>. Without it the
    ///     registration is a <c>TryAdd</c>, which keeps whichever implementation registered first and drops
    ///     this one in silence. Not combined with <see cref="Key" /> or with <c>[Inject]</c> members, which
    ///     register through a factory.
    /// </summary>
    public bool Multiple { get; set; }
}
