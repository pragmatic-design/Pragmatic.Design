using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A collection include's companion read: the next result set's rows with the companion's reader, grouped on <see cref="ChildKey" /> (or,
///     through a join table, on <see cref="LinkKey" />, read beside the child) and handed to each parent by <see cref="ParentKey" />.
/// </summary>
internal sealed record CollectionLoadModel(
    string Name,
    string Parents,
    string Property,
    EquatableArray<LoadStepModel> Children,
    string ChildType,
    PlanReaderModel Reader,
    string GroupType,
    string ChildKey,
    string ParentKey,
    string? LinkKey) : LoadStepModel(Name, Parents, Property, Children);
