namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A table that keeps its history, read with <c>AsOf(utcInstant)</c>: SQL Server's system-versioned table (its DDL), Oracle's Flashback (any
///     table), and on PostgreSQL, SQLite and MySQL the <c>{Table}History</c> table of every version its triggers write (<see cref="SqlTemporal.CreateSql{T}" />).
///     Without it, AsOf reads the current rows on those three (PRAG3012).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TemporalAttribute : Attribute;
