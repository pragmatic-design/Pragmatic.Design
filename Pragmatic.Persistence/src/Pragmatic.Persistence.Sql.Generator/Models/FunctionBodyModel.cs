using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>The implementing part of a <c>[SqlFunction]</c>'s partial method: it throws, since the function runs only as SQL.</summary>
internal sealed record FunctionBodyModel(string Access, string ReturnType, string Name, EquatableArray<string> Parameters);
