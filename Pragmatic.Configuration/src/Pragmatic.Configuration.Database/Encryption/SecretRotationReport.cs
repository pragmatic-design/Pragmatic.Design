namespace Pragmatic.Configuration.Database.Encryption;

/// <summary>Outcome of a key-rotation re-encrypt pass.</summary>
/// <param name="Total">Number of secrets examined.</param>
/// <param name="ReEncrypted">Number successfully re-encrypted under the current key.</param>
/// <param name="Failed">
///     Number that could not be decrypted with any ring key (tampered, or written under a key no longer in
///     the ring) and were therefore left untouched.
/// </param>
public sealed record SecretRotationReport(int Total, int ReEncrypted, int Failed);
