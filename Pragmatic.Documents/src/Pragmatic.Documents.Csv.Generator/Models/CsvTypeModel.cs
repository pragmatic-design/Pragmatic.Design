using Pragmatic.SourceGen;

namespace Pragmatic.Documents.Csv.Generator.Models;

/// <summary>Immutable model for a [CsvSerializable] type.</summary>
/// <remarks>
/// <see cref="Properties"/> uses <see cref="EquatableArray{T}"/> (not a raw ImmutableArray) so record
/// equality compares the elements by value — a raw ImmutableArray compares by reference and would defeat
/// the incremental generator's caching (re-running on every keystroke).
/// </remarks>
internal sealed record CsvTypeModel(
    string Namespace,
    string TypeName,
    string TypeKeyword,
    string Accessibility,
    EquatableArray<CsvPropertyModel> Properties);
