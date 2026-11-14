using Pragmatic.Persistence.Sql;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The predicate of a <c>[Unique(Filter = …)]</c> index on an EF Core provider (#729): the one text the generated schema (what
///     Pragmatic.Migrations builds the database from) and the model's <c>HasFilter</c> both carry, so the two describe one index.
/// </summary>
/// <remarks>
///     The declared filter is in the Sql engine's syntax: identifiers double-quoted, which every provider here reads, and a boolean as
///     <c>{bt:0}</c> / <c>{bt:1}</c>, resolved as the Sql engine resolves it (<see cref="SqlDialects.ResolveBooleanLiterals" />), so the two
///     branches describe the same index on one database. A soft-deletable entity's index holds its live rows too, with the literal the live
///     rows' filter already has on each provider (<c>0</c> in T-SQL, <c>false</c> elsewhere).
/// </remarks>
internal static class UniqueIndexFilters
{
    /// <summary>The index's predicate, or null when it declares none (the live rows of a soft-deleted entity are added elsewhere, as before).</summary>
    public static string? Declared(EntityMetadataModel entity, UniqueIndexModel index, EfCoreProvider provider)
        => index.Filter is null ? null : OnProvider(index.Filter, entity.IsSoftDelete, provider);

    /// <summary><paramref name="filter" /> in <paramref name="provider" />'s SQL, joined with the live rows' when <paramref name="softDelete" />.</summary>
    public static string OnProvider(string filter, bool softDelete, EfCoreProvider provider)
    {
        var dialect = provider switch
        {
            EfCoreProvider.SqlServer => SqlDialect.SqlServer,
            EfCoreProvider.Sqlite => SqlDialect.Sqlite,
            _ => SqlDialect.PostgreSql,
        };
        var declared = SqlDialects.ResolveBooleanLiterals(filter, dialect);
        if (!softDelete)
            return declared;

        var live = provider == EfCoreProvider.SqlServer ? "\"IsDeleted\" = 0" : "\"IsDeleted\" = false";
        return $"{live} AND ({declared})";
    }
}
