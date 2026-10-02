namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Excludes a property from automatic mapping.
///     <para>
///         The generator will not attempt to map this property from the source type.
///         Use this for computed properties or properties populated by other means.
///     </para>
/// </summary>
/// <example>
///     <code>
///     [MapFrom&lt;User&gt;]
///     public partial record UserDto
///     {
///         public int Id { get; init; }
///         public string Email { get; init; }
/// 
///         [MapIgnore]
///         public string ComputedField { get; init; }  // Not mapped
///     }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class MapIgnoreAttribute : Attribute
{
    /// <summary>Ignores the property in both directions.</summary>
    public MapIgnoreAttribute() => Direction = MappingDirection.Both;

    /// <param name="direction">The direction to ignore it in.</param>
    /// <remarks>
    ///     <para>
    ///         For the property that is read and never written — a value the entity computes, a
    ///         column the caller may see and must not set. Before this the only way to say it was two
    ///         DTOs for one shape, which is two places to keep in step and a second type in the
    ///         published contract.
    ///     </para>
    ///     <example>
    ///         <code>
    /// [MapFrom&lt;Order&gt;]
    /// [MapTo&lt;Order&gt;]
    /// public partial record OrderDto
    /// {
    ///     // The entity works it out; a caller sending one is not writing it.
    ///     [MapIgnore(MappingDirection.ToEntity)]
    ///     public decimal Total { get; init; }
    /// }
    /// </code>
    ///     </example>
    /// </remarks>
    public MapIgnoreAttribute(MappingDirection direction) => Direction = direction;

    /// <summary>The direction the property is ignored in.</summary>
    public MappingDirection Direction { get; }
}