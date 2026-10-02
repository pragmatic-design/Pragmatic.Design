namespace Casework.Verify.Infrastructure.Authorization;

/// <summary>
///     The one caller of this service that is not the bus: the system that performs a verification and
///     records what it found.
/// </summary>
/// <remarks>
///     <para>
///         A role held by a <b>service</b> and not by a person, which is why it is named after what it
///         does rather than after a job title. Verify stores no accounts, so nothing here can grant it to
///         anybody: whoever holds a token this service accepts holds this role, and the token comes from
///         the issuer that has the credentials.
///     </para>
///     <para>
///         Two permissions and no more. It may read a verification and answer it; it may not create one
///         — a verification exists because a case asked for it, over the bus, and a service that could
///         create one from outside would be a way to invent work for this service to do.
///     </para>
/// </remarks>
[Role("verification-service", "Reads and answers the verifications this service was asked for")]
[Grants(
    VerifyPermissions.Verification.Read,
    VerifyPermissions.Verification.Update)]
public sealed partial class VerificationServiceRole;
