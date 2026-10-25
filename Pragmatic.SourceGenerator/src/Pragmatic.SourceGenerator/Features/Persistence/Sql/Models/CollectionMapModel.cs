using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A one-to-many or many-to-many side as the runtime path's map describes it. The children are grouped on
///     <see cref="ChildKeyProperties" />, as one composite key when <see cref="CompositeChildKey" />. The link members are set for a
///     many-to-many side; <see cref="LinkChildColumns" /> only when the child's key has several columns, <see cref="ForeignKeyColumns" />
///     only under a composite parent. <see cref="ChildKeySetter" /> sets a child's foreign key from the boxed parent key (<c>c</c>, <c>v</c>), for
///     a one-to-many side whose foreign key is one column of the child; null otherwise.
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
    EquatableArray<string> ForeignKeyColumns,
    string? ChildKeySetter);
