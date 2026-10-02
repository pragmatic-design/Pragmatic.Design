namespace Pragmatic.Mapping;

/// <summary>
///     Which way a mapping goes: reading an entity, writing one, or both.
/// </summary>
/// <remarks>
///     Named after the members it qualifies — <c>FromEntity</c> reads, <c>ToEntity</c> writes — so a
///     declaration reads as the thing it affects rather than as a direction to work out.
/// </remarks>
public enum MappingDirection
{
    /// <summary>Both ways.</summary>
    Both,

    /// <summary>Reading the entity into the DTO — <c>FromEntity</c> and the projection.</summary>
    FromEntity,

    /// <summary>Writing the DTO onto the entity — <c>ToEntity</c>, <c>ApplyTo</c>, a mutation, a patch.</summary>
    ToEntity
}
