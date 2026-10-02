namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a header parameter.
/// </summary>
internal sealed record HeaderParameterModel
{
    /// <summary>
    ///     The header name (e.g., "X-Tenant-Id").
    /// </summary>
    public required string HeaderName { get; init; }

    /// <summary>
    ///     The property name in the endpoint class.
    /// </summary>
    public required string PropertyName { get; init; }

    /// <summary>
    ///     The fully qualified type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the header is required.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the header carries Pragmatic.Validation's <c>[Required]</c>.
    /// </summary>
    /// <remarks>
    ///     Published as required, and nothing else: the binding still reads the value as optional and
    ///     validation refuses its absence with 422. <see cref="IsRequired" /> is the binding's own
    ///     requiredness.
    /// </remarks>
    public bool IsRequiredByValidation { get; init; }

    /// <summary>
    ///     Documentable default from the property initializer (manifest/OpenAPI only).
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    ///     Whether the property has an <c>init</c> accessor, so it can only be set in the object
    ///     initializer.
    /// </summary>
    public bool IsInitOnly { get; init; }

    /// <summary>
    ///     What an optional <c>init</c> property is given when the header is absent: its declared
    ///     initializer, or <c>default!</c> when it declares none. <c>null</c> when the initializer is not
    ///     something the generated file can reproduce (PRAG0536).
    /// </summary>
    public string? InitOnlyFallback { get; init; }

    /// <summary>
    ///     Which <c>RequestBinder</c> overload converts the raw request value to this type.
    /// </summary>
    /// <remarks>
    ///     Set from the symbol where there is one. The endpoints this generator assembles itself
    ///     (Resource CRUD, traits) have none, so an unset value falls back to the type's name.
    /// </remarks>
    public BindKind BindKind
    {
        get => field == BindKind.Unset ? BindKindNames.FromTypeName(TypeName) : field;
        init;
    }
}
