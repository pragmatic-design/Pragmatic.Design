namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a query parameter.
/// </summary>
internal sealed record QueryParameterModel
{
    /// <summary>
    ///     The query parameter name.
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
    ///     Whether the parameter is required.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    ///     Whether the query value carries Pragmatic.Validation's <c>[Required]</c>.
    /// </summary>
    /// <remarks>
    ///     Published as required, and nothing else: the binding still reads the value as optional and
    ///     validation refuses its absence with 422. <see cref="IsRequired" /> is the binding's own
    ///     requiredness.
    /// </remarks>
    public bool IsRequiredByValidation { get; init; }

    /// <summary>
    ///     Default value if not required.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    ///     The declared initializer as an expression the generated file can compile, or <c>null</c> when
    ///     it is not a constant this can reproduce.
    /// </summary>
    /// <remarks>
    ///     <c>DefaultValue</c> is the literal's <i>text</i>, which documentation wants and code cannot
    ///     use: an enum member arrives as <c>Pending</c> with no type in front of it, and a string
    ///     arrives without its quotes. This one is written to be pasted into generated source.
    /// </remarks>
    public string? DefaultValueExpression { get; init; }

    /// <summary>
    ///     Whether the property has an <c>init</c> accessor, so it can only be set in the object
    ///     initializer.
    /// </summary>
    public bool IsInitOnly { get; init; }

    /// <summary>
    ///     What an optional <c>init</c> property is given when the value is absent: its declared
    ///     initializer, or <c>default!</c> when it declares none. <c>null</c> when the initializer is not
    ///     something the generated file can reproduce (PRAG0536).
    /// </summary>
    public string? InitOnlyFallback { get; init; }

    /// <summary>
    ///     Whether the parameter type is a value type (needs .Value for nullable).
    /// </summary>
    public bool IsValueType { get; init; }

    /// <summary>
    ///     Whether this is a [ComplexFilter] parameter (received as JSON string, deserialized at runtime).
    /// </summary>
    public bool IsComplexFilter { get; init; }

    /// <summary>
    ///     The fully qualified type to deserialize into when <see cref="IsComplexFilter"/> is true.
    /// </summary>
    public string? ComplexFilterTypeName { get; init; }

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
