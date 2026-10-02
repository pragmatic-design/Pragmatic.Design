namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Declares a type-safe dependency on another module (library-level).
/// </summary>
/// <typeparam name="TModule">The module type this module depends on.</typeparam>
/// <remarks>
///     <b>Where this is consumed.</b> <c>ModuleTransform</c> reads it while building the module
///     graph, and <c>MetadataReader.DomainModules</c> reads the same relation back out of referenced
///     assemblies so a Host sees modules it does not compile. Cycle detection belongs to the
///     generator, not to this attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class IncludeModuleAttribute<TModule> : Attribute
    where TModule : class
{
}
