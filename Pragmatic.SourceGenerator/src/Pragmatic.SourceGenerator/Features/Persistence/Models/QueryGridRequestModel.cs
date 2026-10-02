namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a query property whose type is the canonical <c>GridFilterRequest</c>.
/// </summary>
/// <remarks>
///     <para>
///         Recognised by type rather than by an attribute, for the same reason a
///         <see cref="QuerySpecificationModel" /> is: the type already says what the property is, and a
///         marker would be a second way to say the same thing.
///     </para>
///     <para>
///         It is <b>not</b> a filter over a column. Left to the ordinary rules a nullable property of a
///         query becomes one by convention, and the generated <c>Apply</c> compared the entity against a
///         field it does not have — which is why the request could only ever be applied by hand, inside
///         an action, over <c>Query()</c>.
///     </para>
///     <para>
///         ⚠️ The request carries its own paging, and the bridge applies it. A query that also declares
///         <c>Page</c>/<c>PageSize</c> would page twice over the same rows, so PRAG0724 says so.
///     </para>
/// </remarks>
internal sealed record QueryGridRequestModel
{
    /// <summary>The property name on the query class (e.g. "Grid").</summary>
    public required string PropertyName { get; init; }

    /// <summary>Whether the property type is nullable, and so has to be guarded before use.</summary>
    public required bool IsNullable { get; init; }
}
