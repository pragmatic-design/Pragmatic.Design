using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     Extension methods for <see cref="Guid" />.
/// </summary>
public static class GuidExtensions
{
    /// <param name="guid">The GUID to convert.</param>
    extension(Guid guid)
    {
        /// <summary>
        ///     Converts a GUID to a URL-safe base64 string (22 characters).
        /// </summary>
        /// <returns>A 22-character URL-safe base64 string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToShortString()
        {
            return ShortGuid.Encode(guid);
        }

        /// <summary>
        ///     Checks if the GUID is empty (all zeros).
        /// </summary>
        /// <returns>True if the GUID is empty, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEmpty()
        {
            return guid == Guid.Empty;
        }

        /// <summary>
        ///     Checks if the GUID is a valid UUID v7.
        /// </summary>
        /// <returns>True if the GUID is a valid UUID v7, false otherwise.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsGuid7()
        {
            return Guid7.IsVersion7(guid);
        }

        /// <summary>
        ///     Extracts the timestamp from a UUID v7.
        /// </summary>
        /// <returns>The embedded timestamp, or null if not a valid UUID v7.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DateTimeOffset? GetTimestamp()
        {
            return Guid7.GetTimestamp(guid);
        }
    }
}
