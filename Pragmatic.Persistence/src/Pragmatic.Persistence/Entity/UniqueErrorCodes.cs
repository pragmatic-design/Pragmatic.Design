using Pragmatic.Result.Database;

namespace Pragmatic.Persistence.Entity;

/// <summary>
///     A duplicate with the code its index declared (#728): the one step the three engines' units of work take after telling which index a
///     write broke, so a declared code reads the same whichever engine refused the write.
/// </summary>
public static class UniqueErrorCodes
{
    /// <summary>
    ///     <paramref name="duplicate" /> with <see cref="DbConflictError.DeclaredCode" /> set from <paramref name="written" />'s declaration,
    ///     or as it came when the entity declares nothing for that index.
    /// </summary>
    /// <param name="duplicate">The conflict the engine classified, its fields or its constraint filled where known.</param>
    /// <param name="written">The entity the refused write carried, of the type whose index was broken.</param>
    public static DbConflictError Declared(DbConflictError duplicate, object? written)
        => written is IDeclaresUniqueErrorCodes declares && declares.UniqueErrorCode(duplicate.FieldName, duplicate.ConstraintName) is { } code
            ? duplicate with { DeclaredCode = code }
            : duplicate;
}
