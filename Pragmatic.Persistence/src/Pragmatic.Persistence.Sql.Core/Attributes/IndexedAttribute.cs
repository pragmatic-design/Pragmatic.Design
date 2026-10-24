namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On a property: the column has an index, so a WHERE on it does not scan the table (the key is indexed by itself). The
///     stand-in for a project without a schema snapshot: where a committed <c>*.schema.json</c> (what the migrations write) among the
///     AdditionalFiles lists the entity's table, its indexes decide and this attribute is not read. A predicate on a column
///     without an index is PRAG3011 (information).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IndexedAttribute : Attribute
{
    /// <summary>A unique index: the schema the generator emits for Pragmatic.Migrations (<see cref="SqlDatabaseAttribute" />) declares it so.</summary>
    public bool Unique { get; init; }
}
