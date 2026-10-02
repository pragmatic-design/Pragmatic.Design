using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Diff;

/// <summary>
///     Computes the difference between a desired schema (from SG) and the current database schema.
/// </summary>
public interface ISchemaDiffEngine
{
    /// <summary>
    ///     Computes the diff between the desired and current schemas.
    /// </summary>
    /// <param name="desired">The target schema (typically SG-generated).</param>
    /// <param name="current">The current database schema. Null means empty database (all tables are new).</param>
    /// <param name="dropUnknownTables">
    ///     Whether a table present in the database but absent from <paramref name="desired" /> should
    ///     produce a <c>DROP TABLE</c>. Pass false on a database shared with another system, so its
    ///     tables are left alone instead of turning every startup into a blocked breaking change.
    /// </param>
    /// <returns>A <see cref="SchemaDiff" /> with ordered changes and breaking change analysis.</returns>
    SchemaDiff ComputeDiff(SchemaVersion desired, SchemaVersion? current, bool dropUnknownTables = true);
}
