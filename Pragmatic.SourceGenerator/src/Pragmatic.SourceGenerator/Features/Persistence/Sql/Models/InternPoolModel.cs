namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>The pool of an <c>[Interned]</c> column: its static field in <c>EntityMaps</c>, and its key across assemblies.</summary>
internal sealed record InternPoolModel(string Field, string Key);
