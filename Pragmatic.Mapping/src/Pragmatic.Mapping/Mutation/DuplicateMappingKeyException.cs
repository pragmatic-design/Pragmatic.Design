namespace Pragmatic.Mapping.Mutation;

/// <summary>
///     Thrown by <see cref="MutationHelpers.MapOneToMany{TRelatedDto,TRelated,TKey}" /> when the
///     collection synchronization key is not unique on either side.
///     <para>
///         A keyed sync is only well-defined when each key identifies at most one item. Duplicate
///         keys in the entity collection make the entity-to-update ambiguous; duplicate keys in the
///         DTO collection would silently create duplicate children. Both cases are rejected up front
///         with this exception instead of crashing later with a low-level <see cref="System.ArgumentException" />
///         or producing duplicate entities.
///     </para>
/// </summary>
public sealed class DuplicateMappingKeyException : Exception
{
    internal DuplicateMappingKeyException(string side, object key)
        : base($"Duplicate {side} key '{key}' detected while synchronizing a 1:N collection. " +
               "Collection sync keys must be unique on both the DTO and entity sides.")
    {
        Side = side;
        Key = key;
    }

    /// <summary>Which collection contained the duplicate key: "DTO" or "entity".</summary>
    public string Side { get; }

    /// <summary>The offending key value.</summary>
    public object Key { get; }
}
