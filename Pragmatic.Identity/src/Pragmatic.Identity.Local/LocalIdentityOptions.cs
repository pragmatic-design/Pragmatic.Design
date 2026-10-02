namespace Pragmatic.Identity.Local;

/// <summary>
///     Configuration options for the local identity provider.
/// </summary>
public sealed class LocalIdentityOptions
{
    /// <summary>BCrypt work factor for password hashing. Default: 12.</summary>
    public int PasswordWorkFactor { get; set; } = 12;

    /// <summary>Maximum consecutive failed login attempts before lockout. Default: 5.</summary>
    public int MaxFailedLoginAttempts { get; set; } = 5;

    /// <summary>Lockout duration after max failed attempts. Default: 15 minutes.</summary>
    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Password reset token validity duration. Default: 1 hour.</summary>
    public TimeSpan ResetTokenExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Email verification token validity duration. Default: 24 hours.</summary>
    public TimeSpan EmailVerificationTokenExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Minimum password length. Default: 8.</summary>
    public int MinPasswordLength { get; set; } = 8;

    /// <summary>Whether to require email verification before allowing login. Default: false.</summary>
    public bool RequireEmailVerification { get; set; }

    /// <summary>
    ///     Whether authentication flows may reveal account state (existence, inactive, locked, unverified)
    ///     to the caller. Default: <see langword="false" /> — the secure/uniform mode: every login failure
    ///     yields one indistinguishable <c>InvalidCredentialsError</c> (401) and registration of an existing
    ///     email returns the same success shape as a fresh registration, so an attacker cannot enumerate
    ///     accounts by probing the response. The real lockout mechanics still run internally; they are simply
    ///     not surfaced through a distinct status. Set to <see langword="true" /> to restore the legacy
    ///     behaviour with distinct errors (401/403/423 on login, 409 on duplicate registration).
    /// </summary>
    public bool RevealAccountState { get; set; }
}
