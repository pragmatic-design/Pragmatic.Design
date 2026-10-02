namespace Showcase.Booking.Entities;

/// <summary>
///     The scaffolded guest search, answering with the module's own <see cref="GuestDto"/> and
///     searching on the two fields a receptionist actually types.
/// </summary>
/// <remarks>
///     <para>
///         Demonstrates: decorating an operation <c>[Resource]</c> scaffolds. Paging, the handler, the
///         endpoint and the predicate keep coming from the generator; only what is written here changes.
///         Attributes on a partial class combine across its parts, so these are the same lines you would
///         write on a query of your own.
///     </para>
///     <para>
///         Declaring filters replaces the convention rather than adding to it. By default the search
///         exposes every text column — including <c>PreferredLanguage</c>, which nobody searches by, as
///         a substring match. Two named fields, one of them exact, is the shape this resource wants.
///     </para>
///     <para>
///         The list endpoint next to it is left alone on purpose: it still answers with the scaffolded
///         <c>GuestListItemDto</c>, which is what makes this a per-operation choice rather than a
///         per-resource one.
///     </para>
/// </remarks>
[ReturnsDto<GuestDto>]
[RequirePermission(BookingPermissions.Guest.Read)]
public partial class ResourceSearchGuestQuery
{
    [Filter(Operator = FilterOperator.Contains)]
    public string? LastName { get; init; }

    [Filter(Operator = FilterOperator.Equals)]
    public string? Nationality { get; init; }
}
