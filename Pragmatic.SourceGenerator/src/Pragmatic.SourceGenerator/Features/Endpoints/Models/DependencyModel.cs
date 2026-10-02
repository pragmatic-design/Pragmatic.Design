namespace Pragmatic.SourceGenerator.Features.Endpoints.Models;

/// <summary>
///     Model representing a dependency (private field) that needs DI.
/// </summary>
internal sealed record DependencyModel
{
    /// <summary>
    ///     The field name (e.g., "_db").
    /// </summary>
    public required string FieldName { get; init; }

    /// <summary>
    ///     The fully qualified type name.
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     Whether the field is readonly.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>
    ///     The boundary this service is registered under, when it is one of the two a boundary
    ///     registers keyed by its own type; <c>null</c> for everything else.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The Actions model carries a field of the same name for the same reason
    ///     (<c>Features/Actions/Models/ActionModel.cs:368</c>), because there are two
    ///     <c>DependencyModel</c> records and this feature uses its own. Whoever unifies them keeps
    ///     this: without it, <c>DbContext</c> and <c>IUnitOfWork</c> are asked for unkeyed and nothing
    ///     answers.
    /// </remarks>
    public string? KeyedServiceType { get; init; }
}
