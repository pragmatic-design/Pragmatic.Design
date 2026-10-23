using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A Pragmatic entity's read repository and repository on the Sql engine, with its declared queries' cases, plain and paged.</summary>
internal sealed record ReadRepositoryModel(
    string Hint,
    string? Namespace,
    string EntityName,
    string EntityType,
    EquatableArray<RunCaseModel> Queries,
    EquatableArray<RunCaseModel> PagedQueries);
