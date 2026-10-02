using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Pragmatic.Result;
using Pragmatic.Result.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.UnitOfWork;

/// <summary>
///     The rule a failed save broke, as the error the caller is told — with a delete refused by a relation
///     told apart from every other foreign-key violation.
/// </summary>
/// <remarks>
///     <para>
///         <c>ClassifyRuleViolation</c> in <c>Pragmatic.Result.EFCore</c> answers a foreign-key violation
///         with <see cref="DbConstraintError" />, a 400, and cannot do better: it sees the provider's words,
///         not the model. A delete that a restricting relation refuses is not a malformed request — the row
///         exists, and something else holds it — so here, where the context and its model are at hand, it
///         becomes <see cref="DbInUseError" />: a 409 naming the entity and the one that uses it.
///     </para>
///     <para>
///         A <b>unique</b> violation is turned the same way: the provider names the index and the model
///         says which properties it covers, so a duplicate answers <c>WorkEmail</c> rather than
///         <c>IX_Employees_WorkEmail</c> — which is what a form can mark and a database name is not.
///     </para>
///     <para>
///         The relation is found by the violated constraint's name among the model's foreign keys. A
///         provider that does not name it (SQLite) leaves the model to answer: the one relation pointing at
///         the deleted type, when there is exactly one. With several and no name, what uses the row is not
///         guessed — the conflict is still reported, without it.
///     </para>
/// </remarks>
internal static class RuleViolationClassifier
{
    public static IError Classify(DbContext db, DbUpdateException exception)
    {
        var error = db.ClassifyRuleViolation(exception);

        if (error is DbConflictError { FieldName: null, ConstraintName: { } violated } duplicate)
            return NameTheFields(db, exception, duplicate, violated);

        if (error is not DbConstraintError { ConstraintType: "ForeignKey" } foreignKeyViolation)
            return error;

        var deleted = exception.Entries
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Metadata)
            .ToList();
        if (deleted.Count == 0)
            return error;

        var relations = db.Model.GetEntityTypes()
            .SelectMany(type => type.GetDeclaredForeignKeys())
            .Where(fk => !fk.IsOwnership && deleted.Any(type => IsOrDerivesFrom(type, fk.PrincipalEntityType)))
            .ToList();

        var relation = foreignKeyViolation.ConstraintName is { } name
            ? Named(relations, name)
            : relations.Count == 1 ? relations[0] : null;

        // A named constraint that is not a relation into a deleted row is some other write's violation —
        // an insert naming a missing row, in the same save. Left as it was.
        if (relation is null && foreignKeyViolation.ConstraintName is not null)
            return error;

        return new DbInUseError
        {
            EntityType = (relation?.PrincipalEntityType ?? deleted[0]).ClrType.Name,
            UsedBy = relation?.DeclaringEntityType.ClrType.Name
        };
    }

    /// <summary>
    ///     The duplicate, with the <b>properties</b> the violated unique index covers instead of the
    ///     index's own name.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ A constraint name is not a field name, and a client that wants to mark the field that
    ///         collided cannot use <c>IX_Employees_WorkEmail</c>. The model knows which properties that
    ///         index covers, and this is the layer that has the model — the same trade the foreign-key
    ///         branch above makes.
    ///     </para>
    ///     <para>
    ///         <b>All of them, joined, for a composite index.</b> Naming only the first would be a wrong
    ///         answer rather than a partial one: the collision is the combination, and a form told to
    ///         mark one of two fields marks the wrong thing half the time.
    ///     </para>
    ///     <para>
    ///         When nothing in the model matches the name, the error is returned as it came: it still
    ///         carries the constraint, which is more than the caller had before and is honest about
    ///         being a database name.
    ///     </para>
    /// </remarks>
    private static IError NameTheFields(
        DbContext db, DbUpdateException exception, DbConflictError duplicate, string constraintName)
    {
        var written = exception.Entries
            .Select(e => e.Metadata)
            .Distinct()
            .ToList();

        // The entries a failed save carries are the ones it tried to write; with none — some providers
        // report a violation without them — the whole model is the candidate set.
        var candidates = written.Count > 0
            ? written
            : [.. db.Model.GetEntityTypes()];

        var matched = candidates
            .SelectMany(type => type.GetIndexes())
            .Where(index => index.IsUnique && Covers(index, constraintName))
            .ToList();

        if (matched.Count != 1)
            return duplicate;

        var fields = string.Join(", ", matched[0].Properties.Select(p => p.Name));

        return duplicate with
        {
            FieldName = fields,
            Reason = $"Duplicate value for {fields}.",
        };
    }

    /// <summary>Whether the database reported this index, by its name or by what it is made of.</summary>
    /// <remarks>
    ///     ⚠️ Not by <c>GetDatabaseName()</c> alone, for the reason the foreign-key lookup below gives:
    ///     the schema is created by Pragmatic.Migrations and EF's model keeps its own name, so the two
    ///     conventions need not agree. Both carry the table and the columns, which identify the index
    ///     whichever named it.
    /// </remarks>
    private static bool Covers(IIndex index, string constraintName)
    {
        if (index.GetDatabaseName() == constraintName)
            return true;

        return index.DeclaringEntityType.GetTableName() is { } table
               && constraintName.Contains(table, StringComparison.Ordinal)
               && index.Properties.All(p => p.GetColumnName() is { } column
                                            && constraintName.Contains(column, StringComparison.Ordinal));
    }

    /// <summary>The relation the database reported by name.</summary>
    /// <remarks>
    ///     ⚠️ Not by <c>GetConstraintName()</c> alone. The schema is created by Pragmatic.Migrations, which
    ///     names a foreign key <c>FK_{Dependent}_{Columns}_{Principal}</c>, and EF's model keeps its own
    ///     <c>FK_{Dependent}_{Principal}_{Columns}</c>: the name the database reports never matched the
    ///     model, and every refused delete in Time off stayed a 400. Both conventions carry the dependent's
    ///     table and the key's columns, so those identify the relation whichever named it.
    /// </remarks>
    private static IForeignKey? Named(List<IForeignKey> relations, string name)
    {
        var exact = relations.FirstOrDefault(fk => fk.GetConstraintName() == name);
        if (exact is not null)
            return exact;

        var matching = relations
            .Where(fk => fk.DeclaringEntityType.GetTableName() is { } table
                         && name.Contains(table, StringComparison.Ordinal)
                         && fk.Properties.All(p => p.GetColumnName() is { } column
                                                  && name.Contains(column, StringComparison.Ordinal)))
            .ToList();

        return matching.Count == 1 ? matching[0] : null;
    }

    private static bool IsOrDerivesFrom(IEntityType type, IEntityType principal)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current == principal)
                return true;
        }

        return false;
    }
}
