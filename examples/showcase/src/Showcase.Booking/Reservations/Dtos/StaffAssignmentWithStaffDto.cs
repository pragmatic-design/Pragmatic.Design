namespace Showcase.Booking.Dtos;

/// <summary>
/// A staff assignment with the name of the person it names.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ No <c>[MapFrom]</c> and no <c>[GenerateProjection]</c>, deliberately: neither could
///         produce this shape. <c>StaffAssignment.StaffId</c> is a plain <c>Guid</c> written from a
///         token, with no <c>[Relation]</c> and therefore no navigation — so there is nothing for
///         <c>Include</c> to load and nothing for a projection to flatten across. The query declares
///         <c>[Join&lt;AppUser&gt;(ForeignKey = "StaffId")]</c> and the generated step builds this
///         type from both sources.
///     </para>
///     <para>
///         The <c>Staff</c> prefix is the join's <c>Alias</c>: <c>StaffDisplayName</c> reads
///         <c>AppUser.DisplayName</c>. A name neither side answers would be PRAG0740 at the
///         declaration, not a compiler error inside a generated file.
///     </para>
/// </remarks>
public sealed class StaffAssignmentWithStaffDto
{
    public Guid Id { get; init; }

    public Guid StaffId { get; init; }

    public Guid PropertyId { get; init; }

    public string Role { get; init; } = "";

    public DateTimeOffset ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>
    ///     The staff member's name, or null when no user carries that id.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Null is the point of the left join. <c>StaffId</c> has no foreign key behind it — a rota
    ///     can name somebody before they have an account, and an account can be removed while the
    ///     history stays — so an inner join would make those rows disappear from the rota instead of
    ///     showing them without a name.
    /// </remarks>
    public string? StaffDisplayName { get; init; }

    /// <summary>The staff member's department, on the same terms.</summary>
    public string? StaffDepartment { get; init; }
}
