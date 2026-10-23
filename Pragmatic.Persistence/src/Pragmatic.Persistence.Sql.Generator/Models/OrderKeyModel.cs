using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A compiled order key: its name, and its text per dialect (SQLite, PostgreSQL, SQL Server, MySQL, Oracle).</summary>
internal sealed record OrderKeyModel(string Key, EquatableArray<string> Texts);
