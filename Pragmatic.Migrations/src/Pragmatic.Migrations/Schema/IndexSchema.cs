using System.Collections.Immutable;

namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Index definition in a database table schema.
/// </summary>
/// <param name="Name">Index name (e.g. "IX_Guests_Email").</param>
/// <param name="Columns">Ordered list of column names in the index.</param>
/// <param name="IsUnique">Whether this is a unique index.</param>
/// <param name="Filter">Optional filter expression (e.g. "\"IsDeleted\" = false" for partial indexes).</param>
public sealed record IndexSchema(
    string Name,
    ImmutableArray<string> Columns,
    bool IsUnique = false,
    string? Filter = null);
