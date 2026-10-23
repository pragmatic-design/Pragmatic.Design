using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A to-one navigation as the runtime path's map describes it: the join column, and the target read at an offset
///     (<see cref="TargetConstruct" />, over <c>r</c>, <c>o</c> and <c>d</c>; null when the target is not an entity the generator could read).
///     <see cref="ForeignKeyColumns" /> is empty but for a composite navigation, one column per key column of its target.
/// </summary>
internal sealed record NavigationMapModel(
    string OwnerType,
    string Property,
    string JoinColumn,
    bool Required,
    string TargetType,
    string? TargetConstruct,
    EquatableArray<string> ForeignKeyColumns);
