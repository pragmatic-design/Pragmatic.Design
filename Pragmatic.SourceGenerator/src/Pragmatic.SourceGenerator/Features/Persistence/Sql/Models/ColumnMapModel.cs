using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A column as the runtime path's map describes it. <see cref="Read" /> is the column's reader over <c>r</c>, <c>i</c> and <c>d</c>;
///     <see cref="Converter" /> and <see cref="ConvertedType" /> give the boxed conversion towards the column, when the column does not store
///     the property's type.
/// </summary>
internal sealed record ColumnMapModel(
    string Property,
    string Column,
    string Kind,
    string Read,
    string? Converter,
    string? ConvertedType,
    bool IsJson,
    EquatableArray<JsonRenameModel> JsonRenames,
    string? FullTextTable,
    string? FullTextKey);
