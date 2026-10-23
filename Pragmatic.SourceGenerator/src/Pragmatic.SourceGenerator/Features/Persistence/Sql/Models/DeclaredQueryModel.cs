using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A <c>[Query]</c> definition as a session extension: the definition's name and parameter types and its PostgreSQL statement for the
///     documentation, the parameters after the chain, the plan's call. <see cref="BatchResultType" /> and <see cref="BatchBody" /> are the
///     overload that records it on a <c>SqlBatch</c>; null where the query cannot join one.
/// </summary>
internal sealed record DeclaredQueryModel(
    string? Namespace,
    string ClassName,
    string MethodName,
    EquatableArray<string> DefinitionParameterTypes,
    string PostgreSql,
    string ReturnType,
    EquatableArray<string> Parameters,
    string Call,
    string? BatchResultType,
    string? BatchBody) : DeclaredSectionModel(Namespace, ClassName);
