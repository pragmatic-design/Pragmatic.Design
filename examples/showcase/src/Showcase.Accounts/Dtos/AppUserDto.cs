using Showcase.Accounts.Entities;

namespace Showcase.Accounts.Dtos;

/// <summary>A user's account, as they see it: who they are and what they chose. The credentials stay in the identity.</summary>
[MapFrom<AppUser>]
[GenerateProjection]
public partial class AppUserDto
{
    public Guid Id { get; init; }
    public string? DisplayName { get; init; }
    public string? Department { get; init; }
    public string? PreferredCulture { get; init; }
}
