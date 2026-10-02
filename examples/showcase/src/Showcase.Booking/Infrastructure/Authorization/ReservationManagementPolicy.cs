using Pragmatic.Authorization.Policy;

namespace Showcase.Booking.Infrastructure.Authorization;

/// <summary>
///     Composable policy: user must be authenticated AND have booking.reservation.create
///     OR be a service principal (internal system call).
/// </summary>
public sealed class ReservationManagementPolicy : ResourcePolicy
{
    public override bool Evaluate(ICurrentUser user)
    {
        // Service principals always pass (internal system calls)
        var isService = HasPrincipalKind(PrincipalKind.Service);
        var hasPermission = IsAuthenticated() & RequirePermission(BookingPermissions.Reservation.Create);

        return (isService | hasPermission).Evaluate(user);
    }
}
