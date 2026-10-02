using Pragmatic.Privacy;

namespace Pragmatic.Identity.Local;

/// <summary>
///     Local identity record with password management.
///     Stored as owned entity (flat in User table) or FK-linked (separate table)
///     depending on whether the domain User declares a property of this type.
/// </summary>
/// <remarks>
///     ⚠️ <b>Everything sensitive on this type is declared</b>, and it is this framework's own type
///     rather than an application's: an application that owns a <c>LocalIdentity</c> on an entity it
///     logs was writing out the sign-in address, the password hash, both bearer tokens and the
///     security stamp in clear, because none of them said anything. The four secrets are
///     <c>[NotLogged]</c> and the address is <c>[PersonalData]</c> — two declarations for two
///     different things, as <see cref="Pragmatic.Serialization.RedactionReason" /> explains: a secret
///     has no data subject and no erasure right, personal data has both.
/// </remarks>
public sealed class LocalIdentity : IdentityRecord
{
    /// <summary>
    ///     The identity provider this record belongs to, as it appears in
    ///     <see cref="IdentityRecord.ExternalIdentityKey" />.
    /// </summary>
    /// <remarks>
    ///     Not the issuer of whatever token an application later mints for the user: this names where
    ///     the identity came from, which for a local sign-in is the local store, whoever signs the
    ///     token afterwards. Conflating the two makes the stored key and the read key differ.
    /// </remarks>
    public const string Provider = "local";

    /// <summary>
    ///     The form an email is stored, keyed and looked up in: lower-cased, invariant culture.
    /// </summary>
    /// <remarks>
    ///     One rule for every side of the store. If registration lower-cased what it wrote while
    ///     sign-in, reset and verification looked up what was typed, a mixed-case sign-in would work or
    ///     fail depending on whether the application's store happened to lower-case too.
    /// </remarks>
    public static string NormalizeEmail(string email) => email.ToLowerInvariant();

    /// <summary>Hashed password (bcrypt embeds its own salt; no separate salt column is needed).</summary>
    /// <remarks>
    ///     ⚠️ <b>Why not <c>[PersonalData]</c>, now that the reader descends here.</b> An entity that
    ///     owns a <c>LocalIdentity</c> asks PRAG2903 about these columns, and the answer
    ///     has to be one of the two. Classifying them as personal data would put them in the <b>subject
    ///     access export</b> — the generated extractor projects every classified field — and an endpoint
    ///     that hands a bcrypt hash back over HTTP is a worse outcome than the one being fixed. They are
    ///     also not what the register is for: a credential authenticates a request, it does not say
    ///     anything about the person. What removes them is the erasure removing the account itself,
    ///     which is what Time off's own erasure asserts ("the account is removed, not closed in place").
    /// </remarks>
    [NotLogged]
    [NotPersonalData(
        "A credential: it authenticates a request and states nothing about the person. Erased with the "
        + "account itself, which the erasure removes rather than clears field by field.")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Email for password reset and verification.</summary>
    /// <remarks>
    ///     The same address the entity above usually classifies on its own face, so it is classified
    ///     here too: which of the two an application happens to log must not decide whether it is
    ///     masked. <c>[PersonalData]</c> and not <c>[NotLogged]</c>, because an address has a data
    ///     subject and an erasure right and the four values above have neither.
    ///     <para>
    ///         ⚠️ PRAG2900 decides reachability to a <c>[DataSubject]</c> within the compilation it runs
    ///         in, and the subject that owns a <c>LocalIdentity</c> is always in the application. So the
    ///         rule skips a compilation that declares no subject at all; otherwise a package could declare
    ///         a secret but not personal data, and writing it here would be an error.
    ///     </para>
    /// </remarks>
    [PersonalData(DataCategory.Contact)]
    public string? Email { get; set; }

    /// <summary>Whether email has been verified.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Email verification token (hashed, short-lived).</summary>
    [NotLogged]
    [NotPersonalData(
        "An opaque, hashed, short-lived value that authorises one operation. It identifies a request, "
        + "not a person, and it is cleared the moment the operation completes.")]
    public string? EmailVerificationToken { get; set; }

    /// <summary>Email verification token expiry.</summary>
    public DateTimeOffset? EmailVerificationTokenExpiresAt { get; set; }

    /// <summary>Password reset token (hashed, short-lived).</summary>
    [NotLogged]
    [NotPersonalData(
        "An opaque, hashed, short-lived value that authorises one operation. It identifies a request, "
        + "not a person, and it is cleared the moment the operation completes.")]
    public string? ResetToken { get; set; }

    /// <summary>Reset token expiry.</summary>
    public DateTimeOffset? ResetTokenExpiresAt { get; set; }

    /// <summary>
    ///     Opaque per-identity stamp embedded into issued tokens (as the <c>sstamp</c> claim) and
    ///     re-checked on every authenticated request. Rotating it (on password change or reset)
    ///     invalidates all previously-issued tokens for this identity.
    /// </summary>
    [NotLogged]
    [NotPersonalData(
        "A random value rotated to revoke issued tokens. It is about the sessions, not about the "
        + "person, and it carries nothing that could be traced back to one.")]
    public string SecurityStamp { get; set; } = string.Empty;

    /// <summary>Number of consecutive failed login attempts.</summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>Lockout end time (null = not locked).</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
}
