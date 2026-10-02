namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a property or method for dependency injection.
///     Properties are set and methods are called automatically after construction.
/// </summary>
/// <remarks>
///     <para>
///         Use this for optional dependencies, circular dependency resolution,
///         or post-construction initialization.
///     </para>
///     <para>
///         The class must be marked with <see cref="ServiceAttribute" />.
///     </para>
///     <para>
///         <b>Method injection:</b> when applied to a method, the source generator resolves
///         each parameter by its declared type from the <c>IServiceProvider</c>, in parameter
///         declaration order. Parameters are always resolved; there is no partial or optional
///         resolution — if any parameter type is unregistered and <see cref="Required"/> is
///         <c>true</c> (default <c>false</c>), an exception is thrown at startup. For method
///         injection, <see cref="Key"/> is applied to the first parameter only; annotate
///         individual parameters with <c>[FromKeyedServices]</c> for keyed resolution on
///         specific parameters.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets whether the injection is required.
    ///     If <c>false</c> (default), injection is optional and <see langword="null"/> is allowed.
    /// </summary>
    /// <remarks>
    ///     The default is <c>false</c> for backward compatibility: a missing service is injected as
    ///     <see langword="null"/> rather than failing. This can hide a misconfiguration and cause a
    ///     <see cref="System.NullReferenceException"/> at first use. Set <c>Required = true</c> to
    ///     fail fast at startup when the service is unregistered.
    ///     <para>
    ///         <b>Design decision:</b> the default is kept <c>false</c> (non-breaking); the opt-in nature
    ///         of <c>Required = false</c> is surfaced by an analyzer rather than by flipping the default.
    ///         That analyzer ships with this package: <c>PRAG1452</c> warns on every <c>[Inject]</c> left
    ///         at <c>Required = false</c>, so the optional contract has to be acknowledged explicitly —
    ///         and in a build that treats warnings as errors, acknowledged in writing.
    ///     </para>
    /// </remarks>
    public bool Required { get; set; }

    /// <summary>
    ///     Gets or sets the service key for keyed service injection.
    /// </summary>
    public string? Key { get; set; }
}
