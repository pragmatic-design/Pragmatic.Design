using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a form parameter bound from form data.
/// </summary>
internal sealed record FormParameterModel
{
    /// <summary>
    ///     The form field name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The property name in the endpoint class.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The fully qualified type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the form field is required.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the form field carries Pragmatic.Validation's <c>[Required]</c>.
    /// </summary>
    /// <remarks>
    ///     Published as required, and nothing else: the binding still reads the value as optional and
    ///     validation refuses its absence with 422. <see cref="IsRequired" /> is the binding's own
    ///     requiredness.
    /// </remarks>
    public bool IsRequiredByValidation { get; init; }

    /// <summary>
    ///     Whether the property is declared nullable — an annotated reference type or a
    ///     <c>Nullable&lt;T&gt;</c> — which makes the field optional to the binding.
    /// </summary>
    /// <remarks>
    ///     Read from the symbol, not from <see cref="TypeName" />: <c>FullyQualifiedFormat</c> renders
    ///     <c>string?</c> as <c>string</c>, since for a reference type the <c>?</c> is an annotation, and a
    ///     test on the name bound every nullable reference field as required.
    /// </remarks>
    public bool IsNullable { get; init; }

    /// <summary>
    ///     Whether this is a file upload (IFormFile / IFormFileCollection).
    /// </summary>
    public bool IsFile { get; init; }

    /// <summary>
    ///     Maximum file size in bytes (from [MaxFileSize] attribute). Null if unconstrained.
    /// </summary>
    public long? MaxFileSize { get; init; }

    /// <summary>
    ///     Allowed MIME content types (from [AllowedContentTypes] attribute). Empty if unconstrained.
    /// </summary>
    public EquatableArray<string> AllowedContentTypes { get; init; } = EquatableArray<string>.Empty;
}
