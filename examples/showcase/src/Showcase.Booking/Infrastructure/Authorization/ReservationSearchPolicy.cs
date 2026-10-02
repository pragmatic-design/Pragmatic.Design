using Pragmatic.Authorization.Policy;

namespace Showcase.Booking.Infrastructure.Authorization;

/// <summary>
///     Who may search reservations: a person holding the read permission, or a service principal.
/// </summary>
/// <remarks>
///     <para>
///         The read-side counterpart of <c>ReservationManagementPolicy</c>, and the Showcase's
///         only example of a policy on a <b>query</b>. <c>PolicyEvaluationFilter</c> is an
///         <c>IActionFilter</c> and a query's invoker does not run the action-filter chain, so the
///         generated query endpoint evaluates the policy itself.
///     </para>
///     <para>
///         It is not a restatement of <c>[RequirePermission(Reservation.Read)]</c>: the alternative
///         branch is what a permission cannot express. A background job or another service calls without
///         a user's permissions and must still be able to read, which is exactly the composition a
///         policy exists for.
///     </para>
/// </remarks>
public sealed class ReservationSearchPolicy : ResourcePolicy
{
    /// <inheritdoc />
    public override bool Evaluate(ICurrentUser user)
    {
        var isService = HasPrincipalKind(PrincipalKind.Service);
        var hasPermission = IsAuthenticated() & RequirePermission(BookingPermissions.Reservation.Read);

        return (isService | hasPermission).Evaluate(user);
    }
}
