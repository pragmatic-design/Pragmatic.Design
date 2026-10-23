namespace Pragmatic.Persistence.Sql;

// ASSUMPTION (2026-10-18): the spike declares its own [Entity] and [Table] so it builds without the
// repository's projects. The generator matches the attributes by simple name ("EntityAttribute",
// "TableAttribute"), so the repository's Pragmatic.Persistence attributes are a drop-in replacement.

/// <summary>Marks a class whose LINQ call sites the generator translates to SQL.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityAttribute : Attribute;

/// <summary>The table an entity maps to, when it is not the type name pluralised; a view with <see cref="ReadOnly" />.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TableAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    public string? Schema { get; init; }

    /// <summary>True for a view (or any source the application never writes): no writer is generated, and an update or delete through a chain is refused at compile time.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>True for a lookup table whose rows are few by nature: a read of every row is not reported (PRAG3021).</summary>
    public bool Small { get; init; }
}

/// <summary>
///     On a <c>float[]</c> property: a vector of <see cref="Dimensions" /> (pgvector <c>vector(n)</c>, SQL Server 2025 <c>VECTOR(n)</c>, JSON text on
///     SQLite). <c>Sql.CosineDistance</c>, <c>Sql.L2Distance</c> and <c>Sql.InnerProduct</c> compare it with another vector (a k-NN is an
///     OrderBy on the distance and a Take); on SQLite they call the application's functions, <see cref="SqlVectors" /> registered on the connection.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VectorAttribute(int dimensions) : Attribute
{
    public int Dimensions { get; } = dimensions;
}

/// <summary>The column a property maps to, when it is not the property name.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ColumnAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>PostgreSQL only: the type the parameter is cast to on writes (<c>jsonb</c>), when the column's type does not take the bound value as is.</summary>
    public string? Cast { get; init; }
}

/// <summary>
///     On a property: the <see cref="ISqlConverter{TModel, TStore}" /> between the property's type and the column's.
///     The generator reads the converter's store type and emits its two functions in the reader, the binders and the
///     predicates' parameters (<c>o.OccurredAt > since</c> binds <c>ToStore(since)</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ConvertAttribute<TConverter> : Attribute
    where TConverter : class;

/// <summary>
///     A type with no table of its own, flattened into its owner's: each mapped property becomes a column named
///     <c>{Property}_{Member}</c> (the repository's <c>OwnsOne</c> convention). An owner property typed <c>T?</c> is optional: its
///     columns are null together, and the object is null when they are. Members are read in predicates and projections as
///     <c>o.Address.City</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OwnedAttribute : Attribute;

/// <summary>
///     On the root of a hierarchy stored in one table (the repository's <c>[Inheritance(Tph)]</c>): the derived <c>[Entity]</c> classes
///     share the root's table, their own columns nullable, and a text column names each row's type. <c>Query&lt;Root&gt;()</c> reads
///     every row as its own type; <c>Query&lt;Derived&gt;()</c> reads the rows of that type; the writers set the discriminator.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InheritanceAttribute : Attribute
{
    public InheritanceAttribute()
    {
    }

    /// <summary>The repository's form: <c>[Inheritance(InheritanceStrategy.Tph, DiscriminatorColumn = "FeeType")]</c>.</summary>
    public InheritanceAttribute(InheritanceStrategy strategy) => Strategy = strategy;

    /// <summary>The discriminator column; <c>Discriminator</c> by default. It holds the type's name.</summary>
    public string Discriminator { get; init; } = "Discriminator";

    /// <summary>The repository's name for <see cref="Discriminator" />; it wins when both are set.</summary>
    public string? DiscriminatorColumn { get; init; }

    /// <summary>
    ///     How the hierarchy is stored: one table (<see cref="InheritanceStrategy.Tph" />, the default), or one table per concrete type
    ///     (<see cref="InheritanceStrategy.Tpc" />: each derived type its own table with the inherited columns; the root's <c>[Table]</c>
    ///     names a view over them, <c>UNION ALL</c> with the type's name as the discriminator, whose DDL the generator emits as
    ///     <c>{Root}View.CreateSql(dialect)</c>; a write through the root goes to the row's concrete table).
    /// </summary>
    public InheritanceStrategy Strategy { get; init; } = InheritanceStrategy.Tph;
}

/// <summary>Where a hierarchy's rows live.</summary>
public enum InheritanceStrategy
{
    /// <summary>Table per hierarchy: one table, a discriminator column, the derived types' columns nullable.</summary>
    Tph = 0,

    /// <summary>Table per concrete type: one table per derived type; the root is a view over them.</summary>
    Tpc = 1,

    /// <summary>
    ///     Table per type: the root's [Table] holds the root's columns for every row, each derived type's [Table] its key and its own columns;
    ///     reads go through the view <c>{RootTable}View</c> (the root LEFT JOIN each derived table, the discriminator computed from which one
    ///     holds the row), a write of a derived row is two statements in one command. One level of derived types.
    /// </summary>
    Tpt = 2,
}

/// <summary>
///     On an entity with a navigation to itself (<c>Parent</c>, with <c>ParentId</c>): the generator emits
///     <c>GetDescendantsBy{Navigation}(key)</c> and <c>GetAncestorsBy{Navigation}(key)</c> on <c>SqlSession</c>, one recursive CTE
///     each (the node itself excluded), as the repository's attribute of the same name does. <see cref="Via" /> names the navigation
///     when the entity has several to itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class GenerateHierarchyAttribute : Attribute
{
    public string? Via { get; init; }
}

/// <summary>
///     A many-to-many side: a <c>List&lt;TOther&gt;</c> whose rows are linked to this entity through a join table of two key columns and
///     no entity of its own (<c>[ManyToMany("TicketLabels", "TicketId", "LabelId")]</c>). In a lambda the collection reads as a one-to-many
///     (<c>Any</c>, <c>All</c>, <c>Count</c>, <c>SelectMany</c>); <c>Include</c> loads it through the join table; a graph save syncs the
///     links to the list (<see cref="IChangeTracking.CollectionsModified" />), and <c>LinkAsync</c> / <c>UnlinkAsync</c> write one link.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class ManyToManyAttribute(string joinTable, string thisKey, string otherKey) : Attribute
{
    /// <summary>The join table.</summary>
    public string JoinTable { get; } = joinTable;

    /// <summary>The join table's column holding this entity's key.</summary>
    public string ThisKey { get; } = thisKey;

    /// <summary>The join table's column holding the other entity's key.</summary>
    public string OtherKey { get; } = otherKey;
}

/// <summary>A property the table does not have (a navigation, a computed value).</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class NotMappedAttribute : Attribute;

/// <summary>
///     The optimistic-concurrency column (<c>int</c> or <c>long</c>): an update or delete carries the version
///     read and matches zero rows when another writer got there first (<see cref="SqlConcurrencyException" />);
///     a successful update increments it, in the row and in the object.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class VersionAttribute : Attribute;

/// <summary>
///     On the key properties, when the key is not the <c>Id</c> property: one attribute per column of a composite key.
///     Writes match on every key column; navigations, includes and identity need a single-column key.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class KeyAttribute : Attribute;

/// <summary>
///     On the key property (<c>int</c> or <c>long</c>): the database assigns it on insert (identity, serial,
///     AUTOINCREMENT). The insert leaves the column out and reads the value back into the object, one row or a batch
///     (<c>RETURNING</c> / <c>OUTPUT INSERTED</c>); an upsert needs a client key and is refused.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class IdentityAttribute : Attribute;

/// <summary>
///     A column the database fills (a default, a computed column, a trigger): never written by inserts, updates or upserts, and read back by
///     key after an insert, and after an update too with <see cref="OnUpdate" /> (a computed column over the others, a modification stamp).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DatabaseGeneratedAttribute : Attribute
{
    public bool OnUpdate { get; init; }
}

/// <summary>
///     A table that keeps its history, read with <c>AsOf(utcInstant)</c>: SQL Server's system-versioned table (its DDL), Oracle's Flashback (any
///     table), and on PostgreSQL, SQLite and MySQL the <c>{Table}History</c> table of every version its triggers write (<see cref="SqlTemporal.CreateSql{T}" />).
///     Without it, AsOf reads the current rows on those three (PRAG3012).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemporalAttribute : Attribute;

/// <summary>
///     A text column with the engine's full-text index: <c>Sql.FullText</c> and <c>Sql.FullTextRank</c> on it are native where the index is there
///     (SQLite's FTS5 table, SQL Server's full-text index, MySQL's FULLTEXT index, Oracle's CONTEXT index: <see cref="SqlFullText.CreateSql{T}" />; every word of the query required, as PostgreSQL's plainto_tsquery;
///     PostgreSQL's tsvector needs none). Without it, every engine but PostgreSQL matches with a case-insensitive LIKE (PRAG3012).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FullTextAttribute : Attribute;

/// <summary>
///     Rows deleted by marking them (the repository's <c>[SoftDelete]</c>, read by name): the entity declares <c>bool IsDeleted</c> and optionally
///     <c>DeletedAt</c> and <c>DeletedBy</c>. Every read leaves deleted rows out (the chain's, its includes', its subqueries'), unless the chain says
///     <c>IncludeDeleted()</c> or <c>WithoutFilters()</c>; a delete is an UPDATE of those columns (the user from <see cref="SqlContext.Current" />) and
///     <c>RestoreAsync</c> brings the row back. With <see cref="Cascade" /> a graph delete marks the [SoftDelete] rows of its collections too.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SoftDeleteAttribute : Attribute
{
    public bool Cascade { get; set; }
}

/// <summary>
///     Who wrote a row and when (the repository's <c>[Auditable]</c>, read by name): the entity declares <c>CreatedAt</c>, <c>CreatedBy</c>,
///     <c>UpdatedAt</c>, <c>UpdatedBy</c> (any of them); every row write stamps them from the clock and <see cref="SqlContext.Current" />'s user, an
///     insert the Created ones (which no update changes after), every write the Updated ones.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditableAttribute : Attribute;

/// <summary>How a <see cref="RollUpAttribute{TChild}" /> aggregates the children.</summary>
public enum RollUpAggregation
{
    /// <summary>The sum of the named child property.</summary>
    Sum = 0,

    /// <summary>The number of children (the child property is ignored: pass an empty string).</summary>
    Count = 1,
}

/// <summary>
///     A stored aggregate the parent keeps over its <typeparamref name="TChild" /> rows (the repository's <c>[RollUp&lt;TChild&gt;]</c>, read by name): every
///     row write of a child (insert, update, upsert, save, delete, a batch, a graph save with its orphans, a graph delete) moves the parent's column
///     in the same transaction, by delta read from the child's table (its rows' contribution subtracted before the write, added after it), so
///     concurrent writers add up. The child's foreign key is <c>{Parent}Id</c>. The column is the database's: the parent's own writes leave it out
///     and an insert reads it back, as <see cref="DatabaseGeneratedAttribute" /> (its DDL gives it a default, 0). A set-based command on the child
///     (ExecuteUpdate, ExecuteDelete, ExecuteInsert, SyncAsync) would bypass it: refused at build time (PRAG3001).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class RollUpAttribute<TChild>(string childProperty, RollUpAggregation aggregation = RollUpAggregation.Sum) : Attribute
    where TChild : class
{
    public string ChildProperty { get; } = childProperty;

    public RollUpAggregation Aggregation { get; } = aggregation;
}

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

/// <summary>
///     A database whose schema the generator emits for Pragmatic.Migrations, on a <c>partial</c> class of the application: it gets
///     <c>public static SchemaVersion Schema(SqlDialect dialect)</c> (PostgreSQL, SQL Server, SQLite: the providers Migrations has), the tables of
///     the assembly's entities stored in it (<see cref="StoredInAttribute{TDatabase}" />, every entity when the assembly declares one database)
///     with their columns, keys, indexes, foreign keys and the join tables of the many-to-many sides. Emitted only when the compilation
///     references Pragmatic.Migrations. What Migrations does not describe (the views of a hierarchy, the history and full-text DDL, the
///     sequences) stays the text the generator and <c>SqlTemporal</c>, <c>SqlFullText</c>, <c>SqlSequences</c> give, run after the migration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SqlDatabaseAttribute : Attribute
{
    /// <summary>The connection string's configuration key (<c>ConnectionStrings:App</c>), carried by the schema for the tools that read it.</summary>
    public string? ConfigKey { get; init; }
}

/// <summary>
///     A unique index over the entity's columns, by property name (the repository's <c>[Unique("PropertyId", "Code")]</c>), in the schema the
///     generator emits for Pragmatic.Migrations. <see cref="Filter" /> makes it partial (<c>"ValidTo" IS NULL</c>: one open row per parent); a
///     [SoftDelete] entity's index holds its live rows only, so a deleted row does not block the next one. Named <c>IX_{Table}_{Columns}</c>
///     unless <see cref="Name" /> says otherwise.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class UniqueAttribute(params string[] properties) : Attribute
{
    public string[] Properties { get; } = properties;

    /// <summary>The partial index's predicate, double-quoted identifiers; a boolean literal as <c>{bt:0}</c>/<c>{bt:1}</c>, each provider's own.</summary>
    public string? Filter { get; init; }

    public string? Name { get; init; }
}

/// <summary>
///     A string column's length (<c>varchar(n)</c>, <c>nvarchar(n)</c>) in the schema the generator emits for Pragmatic.Migrations. Read by name,
///     as DataAnnotations' is: this one carries no validation, so it is trim and AOT clean (DataAnnotations' constructor is IL2026).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MaxLengthAttribute(int length) : Attribute
{
    public int Length { get; } = length;
}

/// <summary>The database an entity's table lives in, when the assembly declares more than one <see cref="SqlDatabaseAttribute" />.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoredInAttribute<TDatabase> : Attribute where TDatabase : class;

/// <summary>
///     On a string property whose values repeat across rows (a status code, a country, a name a page of rows shares): the reader
///     hands out one instance per distinct value from a bounded pool, so the rows retain one string per value instead of one per
///     row (the provider still materializes each value as it reads it: what is saved is what the rows keep, not the allocation).
///     Projections that read the column (<c>o.Customer!.Name</c>) and the runtime path go through the same pool.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class InternedAttribute : Attribute;

/// <summary>
///     On a DTO: the entity it projects. The generator registers the projection (columns and reader) at module
///     start, so a <c>Select</c> into this DTO built at runtime (a grid filter, a dynamic query) can be read
///     without compiling a tree. <c>Project&lt;TDto&gt;()</c> at compile time needs no attribute. Same shape as
///     the repository's Pragmatic.Mapping attribute. On a struct (a record struct) the projection allocates no object per row.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class MapFromAttribute<TSource> : Attribute
    where TSource : class;

/// <summary>
///     On a DTO property: the entity path it reads (<c>"Customer.Name"</c>), when the name does not say it.
///     Same shape as the repository's Pragmatic.Mapping attribute, read by <c>Project&lt;TDto&gt;()</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class MapPropertyAttribute(string sourcePath) : Attribute
{
    public string SourcePath { get; } = sourcePath;
}

/// <summary>
///     On an entity: a rule every read applies (soft delete, active only), declared once. The rule's
///     <see cref="VisibilityRule{T}.ToExpression" /> is translated at compile time and appended to every
///     plan on the entity; <c>WithoutFilters()</c> on a chain reads past it. Same shape as the repository's.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class VisibleWhenAttribute<TRule> : Attribute
    where TRule : class;

/// <summary>
///     On a static method in a static partial class that takes a <c>SqlQuery&lt;T&gt;</c> first and returns a
///     query chain ending in a terminal: the declared form of a query. The generator reads the chain once and
///     emits an extension on <c>SqlSession</c> with the same name and the remaining parameters, whose body
///     binds the parameters' values straight into the prepared command: no expression tree per call. The
///     definition itself is never executed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class QueryAttribute : Attribute;

/// <summary>
///     A lookup table, small and rarely changing (the repository's <c>[Lookup]</c>, read by name: its own attribute works the same): read whole on
///     purpose, so an unbounded read of it is not reported (PRAG3021), as <see cref="TableAttribute.Small" /> says.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LookupAttribute : Attribute;

/// <summary>A visibility rule: one predicate on the entity, as a lambda the generator can read.</summary>
public abstract class VisibilityRule<T>
{
    public abstract System.Linq.Expressions.Expression<Func<T, bool>> ToExpression();
}
