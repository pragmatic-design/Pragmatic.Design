namespace Pragmatic.Configuration;

/// <summary>
///     Write access to a secret store, kept <b>separate</b> from the read-only <see cref="ISecretStore" />.
/// </summary>
/// <remarks>
///     <para>
///         Most consumers only need <see cref="ISecretStore" /> (read-only): secrets are usually
///         provisioned out-of-band (vault UI, CI/CD, infrastructure-as-code). Writing secrets from
///         application code is a privileged operation, so it lives on this dedicated contract that a
///         backend implements only when it genuinely supports programmatic writes — the general
///         read interface every consumer sees stays free of a mutation surface.
///     </para>
///     <para>
///         Implementations store the value encrypted at rest where applicable and record an audit entry.
///     </para>
/// </remarks>
public interface IWritableSecretStore
{
    /// <summary>Creates or updates a secret value.</summary>
    Task SetSecretAsync(string key, string value, string? tenantId = null, CancellationToken ct = default);

    /// <summary>Deletes a secret. Idempotent: deleting a missing secret is a no-op.</summary>
    Task DeleteSecretAsync(string key, string? tenantId = null, CancellationToken ct = default);
}
