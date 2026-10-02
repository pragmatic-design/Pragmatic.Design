using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text;
using static Pragmatic.Ensure.Ensure;

namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     URL-safe base64 encoding of GUID (22 characters, no padding).
/// </summary>
/// <remarks>
///     <para>
///         ShortGuid encodes a 16-byte GUID into a 22-character URL-safe string using
///         base64url encoding (RFC 4648). This is useful for:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>Shorter URLs: 22 chars vs 36 chars for standard GUID format</description>
///         </item>
///         <item>
///             <description>URL-safe: Uses '-' and '_' instead of '+' and '/'</description>
///         </item>
///         <item>
///             <description>No padding: Omits '=' padding characters</description>
///         </item>
///     </list>
///     <para>
///         Example: "550e8400-e29b-41d4-a716-446655440000" → "AISOMOKbQdSnFkRmVUQAAA"
///     </para>
/// </remarks>
public static class ShortGuid
{
    /// <summary>
    ///     The length of a ShortGuid string (22 characters).
    /// </summary>
    public const int StringLength = 22;

    /// <summary>
    ///     Converts a GUID to a URL-safe base64 string (22 characters).
    /// </summary>
    /// <param name="guid">The GUID to encode.</param>
    /// <returns>A 22-character URL-safe base64 string.</returns>
    public static string Encode(Guid guid)
    {
        Span<byte> guidBytes = stackalloc byte[16];
        guid.TryWriteBytes(guidBytes);

        // Base64Url is unpadded: 16 bytes -> exactly 22 chars (no '=' padding).
        Span<byte> base64Bytes = stackalloc byte[StringLength];
        var status = Base64Url.EncodeToUtf8(guidBytes, base64Bytes, out _, out var bytesWritten);

        if (status != OperationStatus.Done || bytesWritten != StringLength)
            throw new InvalidOperationException("ShortGuid encoding failed to produce the expected length.");

        return Encoding.ASCII.GetString(base64Bytes);
    }

    /// <summary>
    ///     Parses a URL-safe base64 string back to a GUID.
    /// </summary>
    /// <param name="shortGuid">The 22-character URL-safe base64 string.</param>
    /// <returns>The decoded GUID.</returns>
    /// <exception cref="ArgumentException">Thrown when the input is not a valid ShortGuid.</exception>
    public static Guid Decode(string shortGuid)
    {
        ThrowIfNullOrWhiteSpace(shortGuid);

        if (!TryDecode(shortGuid, out var guid))
            throw new ArgumentException($"Invalid ShortGuid format. Expected {StringLength} characters.",
                nameof(shortGuid));

        return guid;
    }

    /// <summary>
    ///     Tries to parse a URL-safe base64 string back to a GUID.
    /// </summary>
    /// <param name="shortGuid">The URL-safe base64 string to parse.</param>
    /// <param name="guid">When successful, contains the decoded GUID.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryDecode(string? shortGuid, out Guid guid)
    {
        guid = Guid.Empty;

        if (string.IsNullOrWhiteSpace(shortGuid) || shortGuid.Length != StringLength)
            return false;

        try
        {
            // Base64Url is unpadded: decode the 22 chars directly (no '=' padding to add).
            Span<byte> base64Bytes = stackalloc byte[StringLength];
            Encoding.ASCII.GetBytes(shortGuid, base64Bytes);

            Span<byte> guidBytes = stackalloc byte[16];
            var status = Base64Url.DecodeFromUtf8(base64Bytes, guidBytes, out _, out var bytesWritten);

            if (status != OperationStatus.Done || bytesWritten != 16)
                return false;

            var decoded = new Guid(guidBytes);

            // 22 base64url chars carry 132 bits but a GUID is only 128: the final char has 4 slack
            // bits. Non-canonical inputs (slack bits set) would otherwise decode to the same GUID
            // without round-tripping. Reject them so malformed strings fail instead of corrupting.
            if (Encode(decoded) != shortGuid)
                return false;

            guid = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Checks if a string is a valid ShortGuid format.
    /// </summary>
    /// <param name="value">The string to validate.</param>
    /// <returns>True if the string is a valid ShortGuid, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValid(string? value)
    {
        return TryDecode(value, out _);
    }
}
