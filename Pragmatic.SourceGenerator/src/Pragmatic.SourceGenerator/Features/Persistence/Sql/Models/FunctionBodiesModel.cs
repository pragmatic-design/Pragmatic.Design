using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>The <c>[SqlFunction]</c>s of one static partial class.</summary>
internal sealed record FunctionBodiesModel(string? Namespace, string ClassName, EquatableArray<FunctionBodyModel> Functions) : DeclaredSectionModel(Namespace, ClassName);
