namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Marks a property as a cascade source. When this property changes via
///     the generated setter, an <c>EntityPropertyChanged</c> domain event is raised,
///     enabling cascade handlers in other assemblies to react.
/// </summary>
/// <remarks>
///     <para>
///         For intra-project cascades (source and target in the same assembly),
///         event emission is auto-detected from <see cref="CascadeOnAttribute{TSource}" />.
///     </para>
///     <para>
///         Use this attribute when the cascade target is in a <b>different</b> assembly
///         that references the source entity's assembly.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CascadeSourceAttribute : Attribute;
