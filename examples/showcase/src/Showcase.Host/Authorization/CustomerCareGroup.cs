namespace Showcase.Host.Authorization;

/// <summary>
///     Customer care team — combines booking-manager and catalog-viewer application roles.
/// </summary>
public sealed class CustomerCareGroup : IGroup
{
    public static string Name => "customer-care";
    public static string? Description => "Customer care team — handles guest inquiries and bookings";
    public static IReadOnlyList<string> DefaultRoles =>
    [
        BookingManagerRole.Name,
        CatalogViewerRole.Name
    ];
}
