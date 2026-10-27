using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>Pragmatic's read repository and repository for a Pragmatic entity on the documents engine, nested in its partial class.</summary>
/// <param name="Hint">The generated file's hint name.</param>
/// <param name="Namespace">The entity's namespace; null in the global one.</param>
/// <param name="EntityName">The entity's simple name.</param>
/// <param name="EntityType">The entity's fully qualified type.</param>
/// <param name="Queries">The declared queries <c>RunAsync(IQuery)</c> dispatches on.</param>
/// <param name="PagedQueries">The declared queries <c>RunAsync(IPagedQuery)</c> dispatches on.</param>
/// <param name="SoftDelete">A <c>[SoftDelete]</c> entity: its bulk delete marks the documents.</param>
internal sealed record DocReadRepositoryModel(
    string Hint,
    string? Namespace,
    string EntityName,
    string EntityType,
    EquatableArray<DocRunCaseModel> Queries,
    EquatableArray<DocRunCaseModel> PagedQueries,
    bool SoftDelete);
