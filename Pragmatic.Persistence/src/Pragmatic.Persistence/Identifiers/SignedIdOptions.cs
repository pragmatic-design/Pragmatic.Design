namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     Optional host-level holder for the <see cref="SignedId" /> signing key, for applications that
///     prefer to bind/inject the secret once rather than pass it on every call.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="SignedId" /> itself is keyless and stateless: it always takes the key as an
///         explicit parameter. This options type is a convenience for hosts that bind the key from
///         configuration or a secret manager and expose it via the options pattern; it does not add
///         behavior of its own.
///     </para>
///     <para>
///         <b>Key management is the host's responsibility.</b> Populate <see cref="Key" /> from a
///         secure source (e.g. a secret manager), never from source or appsettings checked into
///         version control. Use a cryptographically random secret of at least
///         <see cref="SignedId.RecommendedKeyLength" /> bytes and rotate it per your policy; rotating
///         the key invalidates previously issued tokens.
///     </para>
/// </remarks>
public sealed class SignedIdOptions
{
    /// <summary>
    ///     The secret signing key passed to <see cref="SignedId.Encode(long, System.ReadOnlySpan{byte})" />
    ///     and the corresponding decode methods. Must be non-empty; at least
    ///     <see cref="SignedId.RecommendedKeyLength" /> bytes is recommended.
    /// </summary>
    public byte[] Key { get; set; } = [];

    /// <summary>
    ///     Validates that <see cref="Key" /> is populated with a usable signing key.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when <see cref="Key" /> is shorter than <see cref="SignedId.MinimumKeyLength" />.</exception>
    public void Validate()
    {
        if (Key.Length < SignedId.MinimumKeyLength)
            throw new InvalidOperationException(
                $"{nameof(SignedIdOptions)}.{nameof(Key)} must be set to a signing key of at least " +
                $"{SignedId.MinimumKeyLength} bytes ({SignedId.RecommendedKeyLength} bytes recommended); got {Key.Length}.");
    }
}
