namespace Pragmatic.Privacy;

/// <summary>
///     Supplies the key used to build the blind index that finds a subject by identity.
/// </summary>
/// <remarks>
///     <para>
///         The registry cannot search an encrypted identity — encryption uses a random nonce, so the same
///         email produces different ciphertext every time. It searches a keyed hash of the identity instead.
///     </para>
///     <para>
///         <b>Keyed, not plain.</b> A bare SHA-256 of an email address is not a protection: the space of
///         real email addresses is small enough to enumerate, so anyone holding a copy of the table could
///         recover every identity offline. An HMAC under a key that lives outside the database cannot be
///         attacked that way.
///     </para>
///     <para>
///         The key must therefore not be stored anywhere the table is — same rule as the master
///         encryption key, and for the same reason.
///     </para>
/// </remarks>
public interface ISubjectLookupKeyProvider
{
    /// <summary>Resolves the 32-byte HMAC key.</summary>
    ValueTask<byte[]> GetLookupKeyAsync(CancellationToken ct = default);
}
