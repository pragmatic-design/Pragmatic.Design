using System.Buffers;
using System.Buffers.Text;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     Encodes a numeric or GUID identifier into an unguessable, tamper-evident token using a
///     keyed HMAC-SHA256 signature.
/// </summary>
/// <remarks>
///     <para>
///         <b>This IS suitable as a secure external identifier.</b> A token is
///         <c>Base64Url(payload || HMAC-SHA256(payload, key)[..N])</c>: the raw id bytes followed
///         by a truncated HMAC tag computed over those bytes with a caller-supplied secret key.
///         Without the key an attacker cannot produce a token that <see cref="TryDecode(string?, ReadOnlySpan{byte}, out long)" />
///         will accept, so the ids are unguessable and tamper-evident. Decoding recomputes the tag
///         and compares it in constant time (<see cref="CryptographicOperations.FixedTimeEquals(ReadOnlySpan{byte}, ReadOnlySpan{byte})" />);
///         on any mismatch decoding fails and <b>no id is returned</b> — a forged or altered token
///         is never decoded to a usable value.
///     </para>
///     <para>
///         <b>A valid signature proves integrity, not authorization.</b> Possession of a correctly
///         signed token only proves the id was issued by something holding the key; it does NOT
///         prove the caller is allowed to access the referenced resource. Always enforce
///         authorization on the decoded id, exactly as you would for a raw database id.
///     </para>
///     <para>
///         <b>The token is not encrypted.</b> The id value is carried in the clear (plain base64url
///         payload) and is recoverable by anyone holding a token; "unguessable" above means an
///         attacker cannot FORGE a valid token for a chosen id, not that the id is hidden. The
///         guarantee is unforgeability/tamper-evidence, NOT confidentiality.
///     </para>
///     <para>
///         Contrast with <see cref="OpaqueId" />, which is a reversible XOR + base-conversion
///         obfuscation: cosmetic only, trivially brute-forceable, and explicitly NOT a security
///         boundary. Use <see cref="OpaqueId" /> only to avoid exposing obviously-sequential ids in
///         URLs; use <see cref="SignedId" /> when the ids must be unguessable and tamper-evident.
///     </para>
///     <para>
///         <b>Key management is the host's responsibility.</b> The signing key is supplied
///         explicitly on every call (or centrally via <see cref="SignedIdOptions" />). Use a
///         cryptographically random secret of at least <see cref="RecommendedKeyLength" /> bytes
///         (32 bytes / 256 bits), store it securely (e.g. a secret manager), and rotate it as part
///         of your key-management policy. Rotating the key invalidates previously issued tokens.
///     </para>
/// </remarks>
public static class SignedId
{
    /// <summary>
    ///     The recommended minimum signing-key length in bytes (32 bytes / 256 bits), matching the
    ///     HMAC-SHA256 block/output size.
    /// </summary>
    public const int RecommendedKeyLength = 32;

    /// <summary>
    ///     The hard minimum signing-key length in bytes (16 bytes / 128 bits). Keys shorter than this
    ///     are rejected: a tiny key makes the HMAC brute-forceable from a few (id, token) pairs, which
    ///     defeats the tamper-evidence entirely. Prefer <see cref="RecommendedKeyLength" />.
    /// </summary>
    public const int MinimumKeyLength = 16;

    /// <summary>
    ///     The number of HMAC tag bytes appended to the payload (128-bit tag). Truncating the
    ///     256-bit HMAC to 16 bytes keeps tokens compact while leaving forgery infeasible.
    /// </summary>
    public const int TagLength = 16;

    /// <summary>
    ///     Encodes a non-negative <see cref="long" /> id into a signed, unguessable token.
    /// </summary>
    /// <param name="id">The id to encode. Negative values are rejected.</param>
    /// <param name="key">
    ///     The secret signing key. Must be non-empty; at least <see cref="RecommendedKeyLength" />
    ///     bytes is recommended.
    /// </param>
    /// <returns>A URL-safe, unpadded base64url token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="id" /> is negative.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key" /> is empty.</exception>
    public static string Encode(long id, ReadOnlySpan<byte> key)
    {
        if (id < 0)
            throw new ArgumentOutOfRangeException(nameof(id), id, "Id must be non-negative.");

        ValidateKey(key);

        Span<byte> payload = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(payload, id);

        return EncodeCore(payload, key);
    }

    /// <summary>
    ///     Tries to decode and verify a signed token back to its original <see cref="long" /> id.
    /// </summary>
    /// <param name="token">The token to decode.</param>
    /// <param name="key">The same secret signing key used to encode the token.</param>
    /// <param name="id">When verification succeeds, contains the decoded id; otherwise 0.</param>
    /// <returns>
    ///     <see langword="true" /> only if the token is well-formed AND its signature verifies in
    ///     constant time. On any failure (malformed, wrong length, tampered, wrong key) returns
    ///     <see langword="false" /> and never yields a forged id.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key" /> is empty.</exception>
    public static bool TryDecode(string? token, ReadOnlySpan<byte> key, out long id)
    {
        id = 0;

        ValidateKey(key);

        Span<byte> payload = stackalloc byte[sizeof(long)];
        if (!TryDecodeCore(token, key, payload))
            return false;

        id = BinaryPrimitives.ReadInt64BigEndian(payload);

        // A valid signature can still carry a negative payload only if Encode were bypassed; reject
        // to preserve the Encode contract (non-negative ids).
        if (id < 0)
        {
            id = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Decodes and verifies a signed token, throwing if verification fails.
    /// </summary>
    /// <param name="token">The token to decode.</param>
    /// <param name="key">The same secret signing key used to encode the token.</param>
    /// <returns>The decoded id.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="key" /> is empty, or when the token is malformed, tampered,
    ///     or signed with a different key.
    /// </exception>
    public static long Decode(string token, ReadOnlySpan<byte> key)
    {
        if (!TryDecode(token, key, out long id))
            throw new ArgumentException("Invalid or tampered SignedId token.", nameof(token));

        return id;
    }

    /// <summary>
    ///     Encodes a <see cref="Guid" /> id into a signed, unguessable token.
    /// </summary>
    /// <param name="id">The GUID to encode.</param>
    /// <param name="key">
    ///     The secret signing key. Must be non-empty; at least <see cref="RecommendedKeyLength" />
    ///     bytes is recommended.
    /// </param>
    /// <returns>A URL-safe, unpadded base64url token.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key" /> is empty.</exception>
    public static string Encode(Guid id, ReadOnlySpan<byte> key)
    {
        ValidateKey(key);

        Span<byte> payload = stackalloc byte[16];
        id.TryWriteBytes(payload);

        return EncodeCore(payload, key);
    }

    /// <summary>
    ///     Tries to decode and verify a signed token back to its original <see cref="Guid" /> id.
    /// </summary>
    /// <param name="token">The token to decode.</param>
    /// <param name="key">The same secret signing key used to encode the token.</param>
    /// <param name="id">When verification succeeds, contains the decoded GUID; otherwise <see cref="Guid.Empty" />.</param>
    /// <returns>
    ///     <see langword="true" /> only if the token is well-formed AND its signature verifies in
    ///     constant time; otherwise <see langword="false" /> with no id returned.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key" /> is empty.</exception>
    public static bool TryDecode(string? token, ReadOnlySpan<byte> key, out Guid id)
    {
        id = Guid.Empty;

        ValidateKey(key);

        Span<byte> payload = stackalloc byte[16];
        if (!TryDecodeCore(token, key, payload))
            return false;

        id = new Guid(payload);
        return true;
    }

    /// <summary>
    ///     Decodes and verifies a signed token, throwing if verification fails.
    /// </summary>
    /// <param name="token">The token to decode.</param>
    /// <param name="key">The same secret signing key used to encode the token.</param>
    /// <returns>The decoded GUID.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="key" /> is empty, or when the token is malformed, tampered,
    ///     or signed with a different key.
    /// </exception>
    public static Guid DecodeGuid(string token, ReadOnlySpan<byte> key)
    {
        if (!TryDecode(token, key, out Guid id))
            throw new ArgumentException("Invalid or tampered SignedId token.", nameof(token));

        return id;
    }

    private static string EncodeCore(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> key)
    {
        var tokenByteLength = payload.Length + TagLength;

        Span<byte> tokenBytes = stackalloc byte[16 + TagLength]; // upper bound (largest payload = Guid)
        tokenBytes = tokenBytes.Slice(0, tokenByteLength);

        payload.CopyTo(tokenBytes);
        SignInto(payload, key, tokenBytes.Slice(payload.Length, TagLength));

        var expectedChars = Base64UrlCharLength(tokenByteLength);
        Span<byte> base64 = stackalloc byte[expectedChars];

        var status = Base64Url.EncodeToUtf8(tokenBytes, base64, out _, out var written);
        if (status != OperationStatus.Done || written != expectedChars)
            throw new InvalidOperationException("SignedId token encoding failed.");

        return Encoding.ASCII.GetString(base64.Slice(0, written));
    }

    private static bool TryDecodeCore(string? token, ReadOnlySpan<byte> key, Span<byte> payloadDestination)
    {
        var expectedTokenBytes = payloadDestination.Length + TagLength;

        if (string.IsNullOrEmpty(token))
            return false;

        // Each base64url char encodes 6 bits; the decoded byte count is fixed by the payload type,
        // so the canonical (unpadded) token length is fixed too. Reject any other length up front.
        var expectedChars = Base64UrlCharLength(expectedTokenBytes);
        if (token.Length != expectedChars)
            return false;

        Span<byte> base64 = stackalloc byte[expectedChars];
        if (Encoding.ASCII.GetBytes(token, base64) != expectedChars)
            return false;

        Span<byte> tokenBytes = stackalloc byte[16 + TagLength];
        tokenBytes = tokenBytes.Slice(0, expectedTokenBytes);

        var status = Base64Url.DecodeFromUtf8(base64, tokenBytes, out _, out var decoded);
        if (status != OperationStatus.Done || decoded != expectedTokenBytes)
            return false;

        var payload = tokenBytes.Slice(0, payloadDestination.Length);
        var providedTag = tokenBytes.Slice(payloadDestination.Length, TagLength);

        Span<byte> expectedTag = stackalloc byte[TagLength];
        SignInto(payload, key, expectedTag);

        // Constant-time comparison: never short-circuit on the first differing byte, so the time
        // taken does not leak how many leading tag bytes were guessed correctly.
        if (!CryptographicOperations.FixedTimeEquals(providedTag, expectedTag))
            return false;

        payload.CopyTo(payloadDestination);
        return true;
    }

    private static void SignInto(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> key, Span<byte> tagDestination)
    {
        Span<byte> fullTag = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(key, payload, fullTag);
        fullTag.Slice(0, TagLength).CopyTo(tagDestination);
    }

    // Canonical, unpadded base64url length for a given byte count: 4 chars per full 3-byte group,
    // plus 2 chars for 1 trailing byte or 3 chars for 2 trailing bytes. Independent of any framework
    // padding/rounding conventions.
    private static int Base64UrlCharLength(int byteCount)
    {
        var groups = byteCount / 3;
        var remainder = byteCount % 3;
        return groups * 4 + remainder switch
        {
            0 => 0,
            1 => 2,
            _ => 3
        };
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length < MinimumKeyLength)
            throw new ArgumentException(
                $"A signing key of at least {MinimumKeyLength} bytes is required " +
                $"(at least {RecommendedKeyLength} bytes is recommended); got {key.Length}.",
                nameof(key));
    }
}
