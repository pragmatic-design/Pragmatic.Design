using System.Runtime.CompilerServices;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     Encodes sequential numbers into opaque strings using a shuffled base conversion.
/// </summary>
/// <remarks>
///     <para>
///         OpaqueId is useful for hiding sequential database IDs in URLs while keeping them
///         reversible. Unlike hashing, the encoding is deterministic and reversible.
///     </para>
///     <para>
///         <b>NOT a security boundary.</b> The obfuscation is a reversible XOR with a small,
///         salt-derived key (see <see cref="GetObfuscator" />) plus a public base conversion.
///         It only avoids exposing raw, obviously-sequential IDs; it is trivially brute-forceable
///         and MUST NOT be relied upon to keep IDs secret, to authorize access, or to prevent
///         enumeration. Always enforce authorization on the decoded ID. If you need unguessable,
///         tamper-resistant identifiers, use a random/opaque token (e.g. <see cref="Guid7" />,
///         a signed token, or an authenticated encryption scheme) instead.
///     </para>
///     <para>
///         Features:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>Deterministic: Same input + salt always produces same output</description>
///         </item>
///         <item>
///             <description>Reversible: Can decode back to original number</description>
///         </item>
///         <item>
///             <description>URL-safe: Only uses alphanumeric characters</description>
///         </item>
///         <item>
///             <description>Obscured (not secure): Sequential IDs do not look obviously sequential</description>
///         </item>
///     </list>
/// </remarks>
public sealed class OpaqueId
{
    // Default alphabet (URL-safe, no confusing chars like 0/O, 1/l/I)
    private const string DefaultAlphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const string DefaultSalt = "Pragmatic.Identifiers.OpaqueId.v1";
    private const int MinLength = 6;

    // Static default instance for convenience methods
    private static readonly Lazy<OpaqueId> Default = new(() => new OpaqueId());

    private readonly char[] _alphabet;
    private readonly int _base;
    private readonly long _obfuscator;
    private readonly int[] _reverseAlphabet;

    /// <summary>
    ///     Creates an OpaqueId encoder with the default salt.
    /// </summary>
    public OpaqueId() : this(DefaultSalt)
    {
    }

    /// <summary>
    ///     Creates an OpaqueId encoder with a custom salt.
    /// </summary>
    /// <param name="salt">The salt for encoding. Different salts produce different encodings.</param>
    /// <exception cref="ArgumentException">Thrown when salt is null or empty.</exception>
    public OpaqueId(string salt)
    {
        ThrowIfNullOrWhiteSpace(salt);

        _alphabet = ShuffleAlphabet(DefaultAlphabet, salt);
        _base = _alphabet.Length;

        // Build reverse lookup
        _reverseAlphabet = new int[128];
        Array.Fill(_reverseAlphabet, -1);
        for (var i = 0; i < _alphabet.Length; i++)
            _reverseAlphabet[_alphabet[i]] = i;

        // Create obfuscator from salt hash
        _obfuscator = GetObfuscator(salt);
    }

    /// <summary>
    ///     Encodes a number to an opaque string.
    /// </summary>
    /// <param name="number">The number to encode (must be non-negative).</param>
    /// <returns>The encoded string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when number is negative.</exception>
    public string Encode(long number)
    {
        ThrowIfNegative(number);

        // XOR with obfuscator to make sequential numbers look random
        var obfuscated = number ^ _obfuscator;

        // Ensure positive for encoding
        if (obfuscated < 0)
            obfuscated = -obfuscated;

        // Build the base-N digits least-significant-first. A non-negative long needs at most
        // 13 digits in any base >= 36; 32 is a safe upper bound for the whole encoded string.
        Span<char> digits = stackalloc char[32];
        var digitCount = 0;

        if (obfuscated == 0)
        {
            digits[digitCount++] = _alphabet[0];
        }
        else
        {
            var current = obfuscated;
            while (current > 0)
            {
                digits[digitCount++] = _alphabet[(int)(current % _base)];
                current /= _base;
            }
        }

        // Layout (preserving the original List-insert ordering):
        //   prefix, then P padding chars, then the digits most-significant-first.
        // Padding char i was inserted at index 1 when the buffer length was (digitCount + 1 + i),
        // so the later-inserted pads end up closer to the prefix.
        var prefix = _alphabet[(int)(number % _base)];
        var padCount = Math.Max(0, MinLength - (digitCount + 1));

        Span<char> buffer = stackalloc char[32];
        var pos = 0;
        buffer[pos++] = prefix;

        for (var i = padCount - 1; i >= 0; i--)
        {
            var lengthAtInsert = digitCount + 1 + i;
            var padIndex = (number + lengthAtInsert * 7) % _base;
            buffer[pos++] = _alphabet[(int)padIndex];
        }

        // Digits were collected LSB-first; emit them MSB-first.
        for (var i = digitCount - 1; i >= 0; i--)
            buffer[pos++] = digits[i];

        return new string(buffer.Slice(0, pos));
    }

    /// <summary>
    ///     Decodes an opaque string back to the original number.
    /// </summary>
    /// <param name="encoded">The encoded string to decode.</param>
    /// <returns>The original number.</returns>
    /// <exception cref="ArgumentException">Thrown when the input cannot be decoded.</exception>
    public long Decode(string encoded)
    {
        ThrowIfNullOrWhiteSpace(encoded);

        if (!TryDecode(encoded, out var result))
            throw new ArgumentException("Invalid OpaqueId format.", nameof(encoded));

        return result;
    }

    /// <summary>
    ///     Tries to decode an opaque string back to the original number.
    /// </summary>
    /// <param name="encoded">The encoded string to decode.</param>
    /// <param name="number">When successful, contains the decoded number.</param>
    /// <returns>True if decoding succeeded, false otherwise.</returns>
    public bool TryDecode(string? encoded, out long number)
    {
        number = 0;

        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length < 2)
            return false;

        try
        {
            // The prefix tells us the original number mod base
            var prefixIndex = GetCharIndex(encoded[0]);
            if (prefixIndex < 0)
                return false;

            // Try different padding lengths (padding is added after prefix)
            var content = encoded.Substring(1);

            // Try removing 0 to (MinLength-2) padding chars from start
            for (var paddingRemoved = 0; paddingRemoved <= Math.Max(0, content.Length - 1); paddingRemoved++)
            {
                var dataStart = paddingRemoved;
                var data = content.Substring(dataStart);

                if (string.IsNullOrEmpty(data))
                    continue;

                // Decode base conversion
                long obfuscated = 0;
                var valid = true;

                foreach (var c in data)
                {
                    var index = GetCharIndex(c);
                    if (index < 0)
                    {
                        valid = false;
                        break;
                    }

                    obfuscated = obfuscated * _base + index;
                }

                if (!valid)
                    continue;

                // Reverse the XOR
                var candidate = obfuscated ^ _obfuscator;

                // Handle negative result from XOR
                if (candidate < 0)
                    candidate = obfuscated ^ -_obfuscator;

                // Verify: prefix should match candidate % base
                if (candidate >= 0 && candidate % _base == prefixIndex)
                    // Double-check by encoding
                    if (Encode(candidate) == encoded)
                    {
                        number = candidate;
                        return true;
                    }

                // Try without negation
                if (obfuscated % _base == prefixIndex)
                    if (Encode(obfuscated) == encoded)
                    {
                        number = obfuscated;
                        return true;
                    }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetCharIndex(char c)
    {
        return c < 128 ? _reverseAlphabet[c] : -1;
    }

    /// <summary>
    ///     Encodes a number using the default salt.
    /// </summary>
    /// <param name="number">The number to encode.</param>
    /// <returns>The encoded string.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToOpaque(long number)
    {
        return Default.Value.Encode(number);
    }

    /// <summary>
    ///     Decodes an opaque string using the default salt.
    /// </summary>
    /// <param name="encoded">The encoded string to decode.</param>
    /// <returns>The original number.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long FromOpaque(string encoded)
    {
        return Default.Value.Decode(encoded);
    }

    /// <summary>
    ///     Tries to decode an opaque string using the default salt.
    /// </summary>
    /// <param name="encoded">The encoded string to decode.</param>
    /// <param name="number">When successful, contains the decoded number.</param>
    /// <returns>True if decoding succeeded, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryFromOpaque(string? encoded, out long number)
    {
        return Default.Value.TryDecode(encoded, out number);
    }

    private static char[] ShuffleAlphabet(string alphabet, string salt)
    {
        if (string.IsNullOrEmpty(salt))
            return alphabet.ToCharArray();

        var chars = alphabet.ToCharArray();
        var saltChars = salt.ToCharArray();

        for (int i = chars.Length - 1, v = 0, p = 0; i > 0; i--, v++)
        {
            v %= saltChars.Length;
            p += saltChars[v];
            var j = (saltChars[v] + v + p) % i;

            // Swap
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return chars;
    }

    private static long GetObfuscator(string salt)
    {
        // NEEDS-DECISION: the XOR key lives in ~[12345, 1_000_012_344] (< 2^30), which is small
        // enough to brute-force. This is intentional for the current "obscure, not secure" contract
        // (see the type-level remarks): it must NOT be treated as a security boundary. If OpaqueId
        // is ever required to produce unguessable IDs, replace this scheme with a keyed cipher /
        // signed token — widening the range here alone would NOT make it secure.
        var hash = 0L;
        foreach (var c in salt)
            hash = hash * 31 + c;
        // Keep it positive and in a reasonable range
        return Math.Abs(hash % 1_000_000_000L) + 12345;
    }
}
