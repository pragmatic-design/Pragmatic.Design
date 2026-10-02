namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Marks a method in a <see cref="ServiceFactoryAttribute" /> class as a factory method.
///     The return type determines the registered service type.
/// </summary>
/// <remarks>
///     <b>Where this is consumed.</b> <c>ServiceTypeDetector</c> recognises the attribute and
///     <c>ServiceFactoryTransform</c> reads each marked method, taking the service type from the
///     return type and the lifetime from <see cref="Lifetime"/>. <c>CompositionFeature</c> collects
///     them and emits the registrations; in Host mode it also pulls in the factories contributed by
///     referenced assemblies through <c>MetadataReader.ExtractServiceFactories</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class FactoryAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the service lifetime for the factory-created service.
    ///     Default is <see cref="Lifetime.Scoped" />.
    /// </summary>
    public Lifetime Lifetime { get; set; } = Lifetime.Scoped;
}
