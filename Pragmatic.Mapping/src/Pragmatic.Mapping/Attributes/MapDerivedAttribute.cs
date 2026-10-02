namespace Pragmatic.Mapping.Attributes;

/// <summary>
///     Polymorphic mapping: when the source instance is <typeparamref name="TDerivedSource"/>,
///     <c>FromEntity</c> dispatches to <typeparamref name="TDerivedDto"/>.<c>FromEntity</c> instead of
///     the base mapping. The derived DTO must have <c>[MapFrom&lt;TDerivedSource&gt;]</c> and inherit the
///     base DTO (validated at compile time — PRAG0330). Dispatch follows declaration order: put the
///     most-derived types first. Not honored in EF projections (PRAG0331).
/// </summary>
/// <typeparam name="TDerivedSource">The derived source (entity) type to match at runtime.</typeparam>
/// <typeparam name="TDerivedDto">The derived DTO mapped from <typeparamref name="TDerivedSource"/>.</typeparam>
/// <example>
///     <code>
///     [MapFrom&lt;Animal&gt;]
///     [MapDerived&lt;Dog, DogDto&gt;]
///     [MapDerived&lt;Cat, CatDto&gt;]
///     public partial record AnimalDto { public string Name { get; init; } = ""; }
///
///     [MapFrom&lt;Dog&gt;]
///     public partial record DogDto : AnimalDto { public bool GoodBoy { get; init; } }
///     </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class MapDerivedAttribute<TDerivedSource, TDerivedDto> : Attribute
    where TDerivedSource : class
    where TDerivedDto : class;
