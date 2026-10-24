namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A text column with the engine's full-text index: <c>Sql.FullText</c> and <c>Sql.FullTextRank</c> on it are native where the index is there
///     (SQLite's FTS5 table, SQL Server's full-text index, MySQL's FULLTEXT index, Oracle's CONTEXT index: <see cref="SqlFullText.CreateSql{T}" />; every word of the query required, as PostgreSQL's plainto_tsquery;
///     PostgreSQL's tsvector needs none). Without it, every engine but PostgreSQL matches with a case-insensitive LIKE (PRAG3012).
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class FullTextAttribute : Attribute;
