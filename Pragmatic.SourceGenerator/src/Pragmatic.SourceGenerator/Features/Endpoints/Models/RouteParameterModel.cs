namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a route parameter.
/// </summary>
internal sealed record RouteParameterModel
{
    /// <summary>
    ///     The parameter name as it appears in the route.
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
    ///     Whether the parameter is optional.
    /// </summary>
    public bool IsOptional { get; init; }

    /// <summary>
    ///     Route constraint (e.g., "int", "guid").
    /// </summary>
    public string? Constraint { get; init; }

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
