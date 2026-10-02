namespace Pragmatic.Persistence.Entity;

/// <summary>
///     Declares a domain entity as an API resource with a route segment.
///     The source generator uses this to resolve route prefixes for traits
///     and optionally auto-scaffold CRUD endpoints.
/// </summary>
/// <remarks>
///     <para>
///         The segment becomes the route prefix:
///         <c>/api/{boundary}/{segment}</c>.
///         It must be lowercase kebab-case (e.g. "reservations", "room-types").
///     </para>
///     <example>
///         <code>
/// [Entity]
/// [Resource("reservations")]
/// [HasComments]
/// public partial class Reservation
/// {
///     // → GET /api/booking/reservations/{id}/comments
/// }
///
/// [Entity]
/// [Resource("reservations", Capabilities = ResourceCapabilities.All)]
/// public partial class Reservation
/// {
///     // → Full CRUD auto-generated
/// }
/// </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResourceAttribute : Attribute
{
    /// <summary>
    ///     Creates a new resource attribute with the given route segment.
    /// </summary>
    /// <param name="segment">
    ///     The URL segment for this resource (e.g. "reservations", "room-types").
    ///     Must be lowercase kebab-case.
    /// </param>
    public ResourceAttribute(string segment)
    {
        Segment = segment;
    }

    /// <summary>
    ///     The URL route segment for this resource.
    /// </summary>
    public string Segment { get; }

    /// <summary>
    ///     Which CRUD operations to auto-scaffold. Default: <see cref="ResourceCapabilities.None"/>.
    /// </summary>
    public ResourceCapabilities Capabilities { get; set; } = ResourceCapabilities.None;

    /// <summary>
    ///     Override the route parameter name for the entity id.
    ///     Default: <c>{segment-singular}Id</c> (e.g. "reservationId").
    /// </summary>
    public string? ParamName { get; set; }
}
