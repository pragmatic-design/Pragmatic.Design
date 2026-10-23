namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>One dialect's arm of a <c>[SqlFunction]</c>'s runtime renderer: the C# concatenation over <c>a[i]</c>, or null where the function has no rendering.</summary>
internal sealed record FunctionArmModel(string Dialect, string? Concatenation);
