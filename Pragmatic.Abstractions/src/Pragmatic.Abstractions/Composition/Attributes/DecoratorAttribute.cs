namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a class as a decorator for a service interface.
///     Decorators wrap the original service and are applied in order.
/// </summary>
/// <remarks>
///     <para>
///         Decorators must implement the same interface as the service they decorate
///         and accept the inner service as a constructor parameter.
///     </para>
///     <para>
///         Multiple decorators are applied in ascending <see cref="Order" /> value,
///         with the lowest order being closest to the original service.
///     </para>
/// </remarks>
/// <remarks>
///     <b>Where this is consumed.</b> <c>CompositionFeature</c> collects decorated classes through
///     <c>ServiceTypeDetector</c> and emits one delegation stub per decorator via
///     <c>DecoratorDelegationTemplate</c> — generated independently of Library/Host mode. The stub
///     forwards every interface member to the wrapped instance, so a decorator only overrides what
///     it actually decorates. Registration order comes from <see cref="Order"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DecoratorAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the decoration order.
    ///     Lower values are applied first (closer to the original service).
    ///     Default is 0.
    /// </summary>
    public int Order { get; set; }
}
