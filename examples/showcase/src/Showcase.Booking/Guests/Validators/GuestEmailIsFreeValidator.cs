using Pragmatic.Validation;
using ValidationResult = Pragmatic.Validation.Types.ValidationError;

namespace Showcase.Booking.Validators;

/// <summary>
/// No two guests share an address: the hotel writes to it, and a second guest behind the same one
/// receives somebody else's confirmations.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ This is the shape <c>[AsyncValidate&lt;T&gt;]</c> exists for. The rule is about rows the
///         operation does not load — every other guest — so it costs a query, and it only has
///         anything to say when the address changes. Bound to <c>Guest.Email</c>, the composite runs
///         it on an update that touches the address and skips it on one that touches the phone.
///     </para>
///     <para>
///         In create mode nothing has "changed", so every bound validator runs: a new guest is
///         checked whatever the binding says.
///     </para>
/// </remarks>
[Validator]
public class GuestEmailIsFreeValidator(IReadRepository<Guest> guests) : IAsyncValidator<Guest>
{
    public async Task<ValidationResult> ValidateAsync(Guest guest, CancellationToken ct = default)
    {
        var email = guest.Email;
        var id = guest.Id;

        var somebodyElseHasIt = Spec<Guest>.Where(g => g.Email == email && g.PersistenceId != id);

        return await guests.ExistsAsync(somebodyElseHasIt, ct).ConfigureAwait(false)
            ? ValidationResult.For(nameof(Guest.Email), "validation.guest.email_taken")
            : ValidationResult.Valid;
    }
}
