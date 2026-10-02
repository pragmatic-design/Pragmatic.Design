using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Privacy;

/// <summary>
///     Builds the blind index the registry searches by, and the opaque references it hands out.
/// </summary>
public static class SubjectLookup
{
    /// <summary>Length of a generated subject reference, in bytes before hex encoding.</summary>
    private const int ReferenceBytes = 16;

    /// <summary>
    ///     Builds the keyed hash used to find a subject without storing anything searchable about them.
    /// </summary>
    /// <remarks>
    ///     The subject type is included so the same email as a Customer and as an Employee are two
    ///     different subjects — they are two relationships, and erasing one must not erase the other.
    /// </remarks>
    public static byte[] BlindIndex(byte[] lookupKey, string subjectType, string identifier)
    {
        ArgumentNullException.ThrowIfNull(lookupKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        // Length-prefixed, so ("ab","c") and ("a","bc") cannot produce the same index — otherwise a
        // subject type could be made to collide with an identity by moving one character.
        using var hmac = new HMACSHA256(lookupKey);
        var payload = Encoding.UTF8.GetBytes($"{subjectType.Length}:{subjectType}:{identifier}");
        return hmac.ComputeHash(payload);
    }

    /// <summary>
    ///     Generates a fresh subject reference.
    /// </summary>
    /// <remarks>
    ///     <b>Random, never derived from the identity.</b> A reference computed by hashing an email would
    ///     be reversible by brute force over a small space, and the whole point of the indirection is
    ///     that the reference tells you nothing. Randomness is what makes it opaque; the blind index is
    ///     what makes it findable.
    /// </remarks>
    public static string NewReference() => Convert.ToHexString(
        RandomNumberGenerator.GetBytes(ReferenceBytes)).ToLowerInvariant();
}
