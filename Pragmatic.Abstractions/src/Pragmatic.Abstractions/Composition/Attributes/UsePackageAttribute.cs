namespace Pragmatic.Composition.Attributes;

/// <summary>
///     Imports an external package's entities, actions, and services into this module.
///     The package metadata are fused into the module's metadata at compile-time.
/// </summary>
/// <remarks>
///     <para>
///         Once imported, the package's components (actions, entities, services) become part of
///         the host module as if they were declared directly in it. The SG handles:
///     </para>
///     <list type="bullet">
///         <item>Discovering package metadata (actions, entities, services) from the referenced assembly</item>
///         <item>Fusing package metadata into the module's registration methods</item>
///         <item>Generating EF Core configuration for package entities (OwnsOne or FK)</item>
///         <item>Mapping package endpoints under a dedicated route group</item>
///     </list>
///     <para>
///         Each package type can only be imported once per module. Multiple different packages
///         can be imported into the same module.
///     </para>
/// </remarks>
/// <typeparam name="TPackage">
///     The package definition type. Must implement <see cref="IPackageDefinition" />.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class UsePackageAttribute<TPackage> : Attribute
    where TPackage : class, IPackageDefinition
{
    /// <summary>
    ///     Overrides the default route prefix defined in <typeparamref name="TPackage" />.
    ///     When null, the package's <see cref="IPackageDefinition.RoutePrefix" /> is used.
    /// </summary>
    public string? RoutePrefix { get; set; }
}
