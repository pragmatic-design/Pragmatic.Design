using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing a property with validation attributes.
/// </summary>
internal sealed record PropertyValidationModel
{
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The name this property travels under, when <c>[JsonPropertyName]</c> renames it.
    /// </summary>
    /// <remarks>
    ///     Carried so the error can be published under the name the caller knows. Only the explicit
    ///     rename: a naming policy is a serialisation choice this side cannot see, and the writer
    ///     applies it to the C# name when this is null.
    /// </remarks>
    public string? WireName { get; init; }
    public required string PropertyType { get; init; }
    public bool IsNullable { get; init; }

    /// <summary>
    ///     True when the property is a non-nullable value type (e.g. <c>DateTime</c>, <c>int</c>,
    ///     <c>Guid</c>, an enum). Such a value can never be <c>null</c>, so an <c>is null</c> guard
    ///     would be invalid C# (CS0037). Nullable value types (<c>T?</c>) are NOT flagged here.
    /// </summary>
    public bool IsNonNullableValueType { get; init; }

    public bool IsString { get; init; }
    public bool IsCollection { get; init; }
    public string? ElementType { get; init; }
    public bool ElementIsValidatable { get; init; }
    /// <summary>
    ///     Whether the property's type is an enum — what <c>[ValidEnum]</c> needs to be true.
    /// </summary>
    /// <remarks>
    ///     Carried so the rule can be refused before it is written. <c>Enum.IsDefined&lt;T&gt;</c>
    ///     constrains <c>T</c> to a non-nullable value type, so the attribute on anything else emitted
    ///     a <c>CS0453</c> from inside a generated file: an error about a constraint, in code the
    ///     author cannot open, saying nothing about the attribute that caused it.
    /// </remarks>
    public bool IsEnum { get; init; }

    public bool IsNumeric { get; init; }
    public bool IsComparable { get; init; }
    public EquatableArray<ValidationAttributeModel> Attributes { get; init; } =
        EquatableArray<ValidationAttributeModel>.Empty;
    /// <summary>
    ///     Whether the generated validator walks this collection's elements.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <b>Not "the author wrote <c>[ValidateElements]</c>"</b>, which is what its old name —
    ///     <c>HasValidateElements</c> — said. It is <c>declared || (collection &amp;&amp; element is
    ///     validatable)</c>, and the second half is true far more often than the first. A diagnostic
    ///     written against the old name accused every collection of validatable elements of carrying
    ///     an attribute none of them had: measured on six sites, all false. Ask
    ///     <see cref="DeclaresValidateElements" /> for the author's fact.
    /// </remarks>
    public bool ValidatesElements { get; init; }

    /// <summary>Whether the author wrote <c>[ValidateElements]</c> on this property.</summary>
    public bool DeclaresValidateElements { get; init; }

    public bool ValidateElementsStopOnFirst { get; init; }
    public bool IsNestedValidatable { get; init; }
    public bool IsRequiredModifier { get; init; }
}
