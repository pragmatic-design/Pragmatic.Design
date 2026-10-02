namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Declares the <c>Location</c> header template for a 201 Created response. Without it, a Create
///     mutation's 201 points at the <c>Single</c> query that answers at its own route plus
///     <c>/{id}</c>, when there is one, and carries no <c>Location</c> otherwise: any other address is
///     one the generator would have to guess.
/// </summary>
/// <remarks>
///     <para>
///         Tokens of the form <c>{PropertyName}</c> are filled from the properties of the SUCCESS
///         value the action returns (the created entity or DTO), URL-escaped and formatted with the
///         invariant culture. A token naming a property that does not exist fails the build of the
///         generated code — the template is checked at compile time, not at runtime.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/hotels")]
/// [CreatedAt("/api/hotels/{Id}")]
/// public partial record CreateHotel : Mutation&lt;Hotel&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CreatedAtAttribute : Attribute
{
    /// <summary>
    ///     Initializes the attribute with the location template (e.g. <c>"/api/hotels/{Id}"</c>).
    /// </summary>
    /// <param name="locationTemplate">
    ///     The resource URL template; <c>{PropertyName}</c> tokens are filled from the success value.
    /// </param>
    public CreatedAtAttribute(string locationTemplate)
    {
        if (string.IsNullOrWhiteSpace(locationTemplate))
            throw new ArgumentException("Location template must not be null or whitespace.", nameof(locationTemplate));
        LocationTemplate = locationTemplate;
    }

    /// <summary>Gets the location template.</summary>
    public string LocationTemplate { get; }
}
