namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Interface for entities that represent a time-bounded relationship.
///     Example: user-role assignment valid from Jan 1 to Jun 30.
/// </summary>
/// <remarks>
///     <para>
///         Entities implementing this interface get automatic temporal query extensions
///         (<c>Active()</c>, <c>ActiveAt(date)</c>) and FilterMap integration
///         when combined with <c>[TemporalRelation]</c> attribute.
///     </para>
///     <para>
///         A null <see cref="ValidTo"/> means the relation is currently active (no end date).
///     </para>
/// </remarks>
public interface ITemporalRelation
{
    /// <summary>
    ///     When this relation becomes active.
    /// </summary>
    DateTimeOffset ValidFrom { get; set; }

    /// <summary>
    ///     When this relation expires. Null means currently active (no end date).
    /// </summary>
    DateTimeOffset? ValidTo { get; set; }
}
