
// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGen;

/// <summary>
///     An object type the generated context serializes: its ready-to-emit type expression, a stable
///     method-name token, and its properties.
///     <para>
///     Construction: a type with a public parameterless ctor is created with <c>new T()</c>. A type
///     without one (a positional <c>record</c>) is created through an <c>[UnsafeAccessor]</c> constructor
///     using <see cref="ConstructorParamTypes"/> (default args), after which STJ sets the (init) props.
///     Init-only properties are assigned through <c>[UnsafeAccessor]</c> setters (see
///     <see cref="JsonPropertyModel.IsInitOnly"/>).
///     </para>
///     <para>
///     A C# 15 union carries its <see cref="UnionCases"/> and no properties: it is described with System.Text.Json 11's
///     <c>JsonMetadataServices.CreateUnionInfo</c>, written as the case it holds (#838).
///     </para>
/// </summary>
internal sealed record JsonObjectModel(
    string TypeExpr,
    string MethodToken,
    EquatableArray<JsonPropertyModel> Properties,
    bool IsAbstract = false,
    string? DiscriminatorName = null,
    EquatableArray<JsonDerivedTypeModel> DerivedTypes = default,
    bool NeedsUnsafeConstructor = false,
    EquatableArray<string> ConstructorParamTypes = default,
    EquatableArray<string> UnionCases = default)
{
    /// <summary>Whether this is a union, described by its cases.</summary>
    public bool IsUnion => UnionCases.Length > 0;
}
