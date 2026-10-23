namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A filter class nested in an entity, as Pragmatic's filter templates declare it: its constructor's parameters and its bases, its priority,
///     scope and bypass permission, and the body of <c>GetFilter()</c> (an expression body or a block). <see cref="IncludeHistory" />: the
///     temporal filter, followed by the scope that lifts it.
/// </summary>
internal sealed record ShapeFilterModel(string Name, string Parameters, string Bases, int Priority, string Scope, string? Bypass, string GetFilterBody, bool IncludeHistory = false);
