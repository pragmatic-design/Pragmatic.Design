using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>
///     A one-to-many or many-to-many side as the runtime path's map describes it. The children are grouped on
///     <see cref="ChildKeyProperties" />, as one composite key when <see cref="CompositeChildKey" />. The link members are set for a
///     many-to-many side; <see cref="LinkChildColumns" /> only when the child's key has several columns, <see cref="ForeignKeyColumns" />
///     only under a composite parent.
/// </summary>
internal sealed record CollectionMapModel(
    string OwnerType,
    string Property,
    string ChildType,
    string ForeignKeyColumn,
    EquatableArray<string> ChildKeyProperties,
    bool CompositeChildKey,
    string? LinkTable,
    string? LinkChildColumn,
    EquatableArray<string> LinkChildColumns,
    EquatableArray<string> ForeignKeyColumns);
