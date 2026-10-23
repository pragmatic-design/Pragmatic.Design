using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A specification's predicate compiled where it is written: its field in <c>CompiledPredicates</c>, its text per dialect, its values' kinds.</summary>
internal sealed record CompiledPredicateModel(string Field, string Sqlite, string PostgreSql, string SqlServer, string MySql, string Oracle, EquatableArray<string> Kinds);
