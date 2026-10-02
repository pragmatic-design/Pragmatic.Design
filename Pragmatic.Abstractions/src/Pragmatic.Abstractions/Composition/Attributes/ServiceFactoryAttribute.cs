namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a class as a service factory containing factory methods.
///     The factory class itself is registered as a singleton.
/// </summary>
/// <remarks>
///     <b>Where this is consumed.</b> <c>CompositionFeature</c> provides the class to
///     <c>ServiceFactoryTransform</c>, which walks its <see cref="FactoryAttribute"/> methods. The
///     class alone does nothing: a <c>[ServiceFactory]</c> with no <c>[Factory]</c> method inside
///     produces no registration at all.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceFactoryAttribute : Attribute
{
}
