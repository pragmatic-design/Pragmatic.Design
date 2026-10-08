using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>One value a generated UTF-8 JSON writer writes: a member's, or a collection's element.</summary>
/// <param name="Kind">How it is written.</param>
/// <param name="Cast">
///     The <c>WriteNumberValue</c> argument type a number or an enum is widened to; for a
///     <see cref="JsonWriterValueKind.Dictionary" />, the type an integer key is widened to, empty for a string key.
/// </param>
/// <param name="Method">The writer method of an <see cref="JsonWriterValueKind.Object" />, or empty.</param>
/// <param name="Element">
///     The element of a <see cref="JsonWriterValueKind.Collection" />, or the value of a
///     <see cref="JsonWriterValueKind.Dictionary" />; null otherwise.
/// </param>
/// <param name="IsNullableValueType">Whether it is a <c>Nullable&lt;T&gt;</c>, read through <c>.Value</c>.</param>
/// <param name="CanBeNull">Whether it can be null at runtime, and so is checked before it is written.</param>
internal sealed record JsonWriterValueModel(
    JsonWriterValueKind Kind,
    string Cast,
    string Method,
    JsonWriterValueModel? Element,
    bool IsNullableValueType,
    bool CanBeNull)
{
    /// <summary>
    ///     The declared members of an <see cref="JsonWriterValueKind.EnumName" />, each with the name it is written
    ///     as; empty for every other kind.
    /// </summary>
    public EquatableArray<JsonEnumNameModel> EnumNames { get; init; } = EquatableArray<JsonEnumNameModel>.Empty;

    /// <summary>The enum type of an <see cref="JsonWriterValueKind.EnumName" />, fully qualified; empty otherwise.</summary>
    public string EnumType { get; init; } = "";
}
