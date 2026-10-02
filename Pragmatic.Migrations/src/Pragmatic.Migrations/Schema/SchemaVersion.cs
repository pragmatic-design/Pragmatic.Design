using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Pragmatic.Migrations.Schema;

/// <summary>
///     Complete database schema snapshot. Generated at compile-time by the SG,
///     or built at runtime by the schema introspector.
/// </summary>
/// <param name="Tables">All tables in this database.</param>
/// <param name="DatabaseName">Logical database name (e.g. "ShowcaseAppDatabase"). Null for single-database setups.</param>
/// <param name="ProviderName">Database provider (e.g. "PostgreSql", "SqlServer", "Sqlite"). Used to resolve the correct introspector and SQL generator.</param>
/// <param name="ConfigKey">Configuration key for the connection string (e.g. "ConnectionStrings:App"). Used by the CLI for auto-discovery.</param>
public sealed record SchemaVersion(
    ImmutableArray<TableSchema> Tables,
    string? DatabaseName = null,
    string? ProviderName = null,
    string? ConfigKey = null)
{
    private string? _hash;

    /// <summary>
    ///     Canonical identity of this schema — equal for any two schemas the diff engine would
    ///     report as identical, no matter which side produced them.
    ///     <para>
    ///     It is derived, never supplied. If each producer hashed its own text, the
    ///     compile-time schema and the introspected one would never agree even when they describe the
    ///     same database (the generator writes <c>integer</c> where PostgreSQL reports <c>int4</c>)
    ///     and the two could not be compared at all. Both go through
    ///     <see cref="SchemaHasher" />, which applies the same normalisation as the diff.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     Computed on first access and cached. The computation is pure, so a race can only repeat
    ///     work and store the same value.
    /// </remarks>
    [JsonInclude]
    public string Hash => _hash ??= SchemaHasher.Compute(this);
}
