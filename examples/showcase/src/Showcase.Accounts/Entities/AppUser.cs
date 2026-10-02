namespace Showcase.Accounts.Entities;

/// <summary>
///     The application's domain user entity.
///     Identity is a separate infrastructure concern — composed via <see cref="LocalIdentity"/>
///     as an EF Core owned entity (flat in the AppUsers table).
/// </summary>
[Entity]
[Auditable]
[PragmaticUser(MatchClaim = "sub")]
public partial class AppUser : IEntity, ISelfRegisteringUser<AppUser>
{
    /// <summary>
    ///     A user who registers themselves: named after their email until they choose a name. The
    ///     generated <c>AppUser.LocalIdentityStore</c> creates them through this.
    /// </summary>
    public static AppUser Register(LocalIdentity identity)
    {
        var user = Create();
        user.DisplayName = identity.Email;
        user.Identity = identity;
        return user;
    }

    /// <summary>Human-readable display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The department the user belongs to (e.g., "Engineering", "Sales").</summary>
    public string? Department { get; set; }

    /// <summary>URL to the user's avatar.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    ///     Local identity — credentials, password, lockout state.
    ///     Owned by EF Core: flat in the AppUsers table via OwnsOne.
    /// </summary>
    public LocalIdentity? Identity { get; set; }

    // Profile properties — SG generates IUserProfile adapter from these

    /// <summary>The user's preferred culture for localization.</summary>
    [ProfileProperty]
    public string? PreferredCulture { get; set; }

    /// <summary>The user's preferred time zone (IANA format).</summary>
    [ProfileProperty]
    public string? TimeZone { get; set; }

    /// <summary>The user's preferred theme.</summary>
    [ProfileProperty]
    public string? Theme { get; set; }

}
