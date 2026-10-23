using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     The map an entity of this assembly registers at module start: its table and key, its columns, navigations and collections, its rules,
///     its reader and how it writes. <see cref="CompositeKeyProperties" /> is empty unless the key has several columns.
/// </summary>
internal sealed record EntityMapModel(
    string EntityType,
    string Table,
    EquatableArray<string> KeyColumns,
    EquatableArray<ColumnMapModel> Columns,
    EquatableArray<NavigationMapModel> Navigations,
    EquatableArray<CollectionMapModel> Collections,
    EquatableArray<string> RuleTypes,
    string ReaderName,
    string KeySelectorProperty,
    string? FixedPredicate,
    EntityWriteMode WriteMode,
    WriterModel? Writer,
    string? SoftDeleteColumn,
    bool SoftDeleteCascade,
    bool CallerFiltered,
    EquatableArray<string> CompositeKeyProperties,
    EquatableArray<RollUpModel> RollUps);
