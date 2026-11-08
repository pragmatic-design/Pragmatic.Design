using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A Pragmatic entity's read repository and repository on the Sql engine, with its declared queries' cases, plain and paged, and the reads
///     by key with navigations its loads declare, and the reads its <c>[Autocomplete]</c> properties answer. <paramref name="HidesBase" />: the entity
///     derives from another entity, whose own repositories of the same names it hides (declared <c>new</c>). <paramref name="UserMatch" />: the
///     entity is a <c>[PragmaticUser]</c>, and the read repository is its <c>IUserLookup&lt;TUser&gt;</c>, matching this member path (#593).
/// </summary>
internal sealed record ReadRepositoryModel(
    string Hint,
    string? Namespace,
    string EntityName,
    string EntityType,
    EquatableArray<RunCaseModel> Queries,
    EquatableArray<RunCaseModel> PagedQueries,
    EquatableArray<IncludeReadModel> IncludeReads,
    EquatableArray<LinkNavigationModel> Collections,
    EquatableArray<string> Autocompletes,
    bool HidesBase = false,
    string? UserMatch = null);
