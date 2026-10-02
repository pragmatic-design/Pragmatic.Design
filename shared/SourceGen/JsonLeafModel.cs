// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     A leaf type served by a <c>JsonMetadataServices.CreateValueInfo</c> value-info. Carries the
///     ready-to-emit type expression and its converter expression.
/// </summary>
internal sealed record JsonLeafModel(
    string TypeExpr,
    string ConverterExpr);
