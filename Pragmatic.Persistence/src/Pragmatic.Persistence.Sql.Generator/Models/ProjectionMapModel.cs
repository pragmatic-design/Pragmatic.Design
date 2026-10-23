using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A <c>[MapFrom]</c> DTO's declared projection, registered for the runtime path: its columns and joins, its construction over <c>r</c>
///     and <c>d</c>, and its collection members, grouped on <see cref="KeyProperty" /> or, under a composite parent
///     (<see cref="CompositeKey" />), on <see cref="CompositeKeyProperties" />.
/// </summary>
internal sealed record ProjectionMapModel(
    string EntityType,
    string DtoType,
    EquatableArray<string> Columns,
    EquatableArray<string> Joins,
    string Construct,
    EquatableArray<ProjectedCollectionModel> Collections,
    bool CompositeKey,
    EquatableArray<string> CompositeKeyProperties,
    string? KeyProperty);
