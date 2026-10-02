namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a query property marked with [ComplexFilter].
///     The property type is a <c>[FilterDto&lt;TEntity&gt;]</c> class whose generated
///     <c>ApplyFilter()</c> extension method will be called inside the query's <c>Apply()</c> method.
/// </summary>
internal sealed record QueryComplexFilterModel
{
    /// <summary>The property name on the query class (e.g. "Location").</summary>
    public required string PropertyName { get; init; }

    /// <summary>The fully qualified type name of the FilterDto (e.g. "Showcase.Booking.Queries.PropertyLocationFilter").</summary>
    public required string PropertyTypeFullName { get; init; }

    /// <summary>The simple type name (e.g. "PropertyLocationFilter").</summary>
    public required string PropertyTypeName { get; init; }
}
