using Showcase.Billing.Infrastructure.Authorization;
using Showcase.Booking.Infrastructure.Authorization;

namespace Showcase.Host.Authorization;

/// <summary>Application role: billing operations + booking reads.</summary>
/// <remarks>
///     The one role in this host whose grant is declared here rather than assembled in
///     <c>Program.cs</c>. Both halves are lists the modules own and publish with
///     <c>[PermissionSet]</c>, so the role registry — which is read before the application starts,
///     by a role screen and by anything that wants to know what a role is — says the same thing the
///     runtime grants. The roles below it still compose at the builder, which is the other way and
///     stays exercised.
/// </remarks>
public sealed class BillingClerkRole : IRole
{
    public static string Name => "billing-clerk";
    public static string? Description => "Manages invoices, reads booking data";
    public static IReadOnlyList<string> DefaultPermissions =>
        [.. BillingClerk.Permissions, .. BookingReader.Permissions];
}
