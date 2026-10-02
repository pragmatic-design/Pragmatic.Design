namespace Showcase.Booking.Guests.Actions;

/// <summary>
///     The front desk registering a guest <b>for</b> someone else, so the row belongs to that person
///     and not to whoever typed it in.
/// </summary>
/// <remarks>
///     <para>
///         <c>[StartsDelegation]</c> makes the invoker open the delegation around the whole
///         invocation: inside it, <c>ICurrentUser</c> answers with the subject, so the
///         <c>[HasOwner]</c> stamp, the row filters and the audit trail all follow the composed
///         authority. The subject is named by a property, so the action carries who it acts for the
///         way it carries anything else it needs.
///     </para>
///     <para>
///         ⚠️ The action's own <c>[RequirePermission]</c> is still evaluated against the <b>caller</b>,
///         before the scope opens: who may open a delegation and what may be done inside one are
///         different questions. With no grant store, that permission is the only thing between a
///         caller and acting for an arbitrary subject — which is why this one asks for
///         <c>Guest.Create</c> and not for something weaker.
///     </para>
///     <para>
///         ⚠️ Writing <c>ActAs</c> in the body would not be the same thing and would be worse: the
///         body runs after authorization has decided, so the action would be authorized as the caller
///         and executed as the subject.
///     </para>
///     <para>
///         The scope stays open past the body: the row is stamped at <c>SaveChanges</c>, which the
///         pipeline runs afterwards, and a scope closed with the body would leave it owned by the front
///         desk.
///     </para>
/// </remarks>
[DomainAction]
[RequirePermission(BookingPermissions.Guest.Create)]
[StartsDelegation(nameof(ForUserId), Purpose = "front desk registering a guest on their behalf")]
[Endpoint(HttpVerb.Post, "api/guests/on-behalf-of")]
[ApiSummary("Register Guest On Behalf Of")]
[ApiTags("Guests")]
public partial class RegisterGuestOnBehalfOfAction : DomainAction<Guid>
{
    private IRepository<Guest> _guests = null!;

    /// <summary>The person this registration is for: the row ends up theirs.</summary>
    public required string ForUserId { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public required string Email { get; init; }

    public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
    {
        var guest = Guest.Create();

        guest.SetFirstName(FirstName);
        guest.SetLastName(LastName);
        guest.SetEmail(Email);

        _guests.Add(guest);

        // The owner is not set here: the interceptor stamps it from ICurrentUser at save time, and
        // save time is inside the delegation the pipeline holds. Setting it by hand would be the
        // same value written twice, and the one that matters would stop being measured.
        return Task.FromResult(Result<Guid, IError>.Success(guest.Id));
    }
}
