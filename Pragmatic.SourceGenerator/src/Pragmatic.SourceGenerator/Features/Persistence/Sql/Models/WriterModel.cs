using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     The writer of an entity's columns into one table: every column in order, the key columns and the version by position, the keys alone
///     for a delete. A table-per-type entity's root writer carries its next level as <see cref="Part" />, nested. The initializers
///     (<see cref="Stamps" />, the native version's binds) belong to a root writer only.
/// </summary>
internal sealed record WriterModel(
    string EntityType,
    string Table,
    EquatableArray<WrittenColumnModel> Columns,
    EquatableArray<int> KeyIndexes,
    int VersionIndex,
    string? VersionProperty,
    string? VersionRead,
    EquatableArray<string> KeyBinds,
    EquatableArray<string> KeyProperties,
    IdentityModel? Identity,
    EquatableArray<GeneratedColumnModel> Generated,
    bool GeneratedOnUpdates,
    WriterModel? Part,
    StampsModel? Stamps,
    EquatableArray<string> NativeVersionBinds,
    bool HasNativeVersion);
