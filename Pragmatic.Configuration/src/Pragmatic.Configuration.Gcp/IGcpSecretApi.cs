namespace Pragmatic.Configuration.Gcp;

/// <summary>
///     Thin seam over the GCP Secret Manager client, capturing exactly the operations the store needs. Keeps
///     the store unit-testable (fake this interface) while the real implementation is a minimal SDK wrapper.
/// </summary>
internal interface IGcpSecretApi
{
    /// <summary>Reads the latest version of a secret, or <c>null</c> when the secret does not exist.</summary>
    Task<string?> AccessLatestAsync(string secretId, CancellationToken ct);

    /// <summary>Creates the secret if needed and adds a new version with <paramref name="value" />.</summary>
    Task UpsertAsync(string secretId, string value, CancellationToken ct);

    /// <summary>Deletes the secret. Idempotent: a missing secret is a no-op.</summary>
    Task DeleteAsync(string secretId, CancellationToken ct);
}
