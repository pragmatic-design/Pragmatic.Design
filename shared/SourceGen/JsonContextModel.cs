
// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     The full set of types one assembly's generated <c>PragmaticJsonContext</c> covers: object types
///     (with properties), leaf types (value-info) and collection types (collection-info).
///     Value-equatable via <see cref="EquatableArray{T}"/>.
/// </summary>
internal sealed record JsonContextModel(
    string Namespace,
    EquatableArray<JsonObjectModel> Objects,
    EquatableArray<JsonLeafModel> Leaves,
    EquatableArray<JsonCollectionModel> Collections);
