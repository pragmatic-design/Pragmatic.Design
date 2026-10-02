using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Parent DTO for the element-validation sample. Each <see cref="TeamMemberRequest" /> in
///     <see cref="Members" /> is validated individually; failures surface with indexed property paths
///     such as <c>Members[1].Role</c>.
/// </summary>
/// <remarks>
///     ⚠️ <b>And there is no attribute on the list</b>, which is the point of this sample beside its
///     sibling <see cref="CreateInvoiceRequest" />. The elements are walked because
///     <see cref="TeamMemberRequest" /> is an <c>ISyncValidator</c>; <c>[ValidateElements]</c> written
///     bare adds nothing and is <c>PRAG0223</c>.
/// </remarks>
public partial record AddTeamRequest
{
    [Required]
    public required string TeamName { get; init; }

    [Required, MinCount(1)]
    public required List<TeamMemberRequest> Members { get; init; }
}
