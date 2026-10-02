using Pragmatic.Comments;
using Pragmatic.Tags;
using Pragmatic.Notes;
using Showcase.Booking.Validators;

namespace Showcase.Booking.Entities;

/// <summary>
/// A hotel guest who can make reservations.
/// Demonstrates: [Autocomplete] on Email for search-as-you-type.
/// </summary>
[Entity]
[Auditable]
[HasOwner]
[HasAccessScopes]
[ConcurrencyAware]
[Resource("guests", Capabilities = ResourceCapabilities.All)]
[HasNotes]
// Moderated comments: guest-facing feedback is held for approval before anyone can read it.
// Reservation carries the unmoderated variant, so both paths are exercised end to end.
[HasComments(RequireApproval = true, SupportInternalNotes = true)]
// A deliberately small limit: it is what makes the per-entity cap testable under concurrency
// without inserting fifty rows first.
[HasTags(MaxPerEntity = 3)]
[Relation.OneToMany<Reservation>]
[Relation.OneToOne<GuestPreferences>.WithNavigation("Preferences", Inverse = "Guest", IsPrincipal = true)]
public partial class Guest : IEntity
{
    [Required]
    public string FirstName { get; private set; } = "";

    [Required]
    public string LastName { get; private set; } = "";

    /// <summary>
    ///     The address the hotel writes to.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>[AsyncValidate&lt;T&gt;]</c> on the <b>property</b> and not on the class: the rule
    ///     costs a query over every other guest, and it has nothing to say about an update that
    ///     changes a phone number. Bound here, the composite runs it when the address changes and
    ///     skips it when it does not — which is the whole difference between a rule you can afford
    ///     on every write and one you cannot.
    ///     <para>
    ///         Measured by removal: take this attribute away and the validator still
    ///         runs — <c>[Validator]</c> alone registers it — so the <i>refusal</i> of a duplicate
    ///         is unchanged. What the binding buys is the skip, and that is the single test that
    ///         goes red: <c>WhenTheExpensiveRuleRunsTests.ChangingSomethingElse_DoesNotRunTheRuleAtAll</c>.
    ///     </para>
    /// </remarks>
    [Required]
    [Email]
    [Autocomplete]
    [AsyncValidate<GuestEmailIsFreeValidator>]
    public string Email { get; private set; } = "";

    [Phone]
    public string? Phone { get; private set; }

    public string? Nationality { get; private set; }

    /// <summary>Preferred language for communications (ISO 639-1).</summary>
    public string PreferredLanguage { get; private set; } = "en";

}
