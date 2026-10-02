using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Element type validated by [ValidateElements] on <see cref="AddTeamRequest.Members" />.
///     Must be a partial type with validation attributes so the SG emits its <c>Validate()</c>.
/// </summary>
public partial record TeamMemberRequest
{
    [Required, Email]
    public required string Email { get; init; }

    [Required, MinLength(2)]
    public required string Role { get; init; }
}
