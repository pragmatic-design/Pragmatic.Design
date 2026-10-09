// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     One serializable property. <see cref="TypeExpr"/> is the ready-to-emit fully-qualified type
///     expression (e.g. <c>global::App.Foo</c>, <c>global::System.Int32</c>,
///     <c>global::System.Collections.Generic.List&lt;global::System.String&gt;</c>).
/// </summary>
/// <remarks><c>Ignore</c> is the property's <c>[JsonIgnore]</c> condition, when it has one the context must honour.</remarks>
internal sealed record JsonPropertyModel(
    string ClrName,
    string JsonName,
    string TypeExpr,
    bool IsValueType,
    bool IsInitOnly,
    string DeclaringTypeExpr,
    JsonPropertyIgnore Ignore = JsonPropertyIgnore.None);
