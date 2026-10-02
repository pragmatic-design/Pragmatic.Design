namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks an <c>IStartupStep</c> implementation for auto-registration.
///     The source generator discovers classes with this attribute and includes them in the startup pipeline.
/// </summary>
/// <remarks>
///     <para>
///         Classes marked with this attribute that implement <c>IStartupStep</c>
///         will be automatically discovered and registered by the Composition generator.
///     </para>
///     <para>
///         The execution order is determined by the <c>IStartupStep.Order</c>
///         property, not by this attribute.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StartupStepAttribute : Attribute
{
}
