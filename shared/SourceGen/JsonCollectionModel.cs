// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     A collection type (List&lt;T&gt;, T[], Dictionary&lt;K,V&gt;) served by the matching
///     <c>JsonMetadataServices.Create{List|Array|Dictionary}Info</c>. Carries the ready-to-emit
///     <c>typeof</c> expression and the full create-info call.
/// </summary>
internal sealed record JsonCollectionModel(
    string TypeExpr,
    string CreateInfoExpr);
