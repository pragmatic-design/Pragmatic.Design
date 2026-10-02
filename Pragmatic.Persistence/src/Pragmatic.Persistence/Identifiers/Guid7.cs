using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Pragmatic.Persistence.Identifiers;

/// <summary>
///     UUID v7 utility class providing standard and SQL Server-optimized UUID v7 generation.
/// </summary>
/// <remarks>
///     <para>
///         UUID v7 layout (RFC 9562):
///         <code>
///  0                   1                   2                   3
///  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                         unix_ts_ms (32 bits)                  |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |          unix_ts_ms (16 bits) |  ver  |   rand_a (12 bits)    |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |var|                       rand_b (62 bits)                    |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// |                          rand_b (32 bits)                     |
/// +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
/// </code>
///     </para>
///     <para>
///         The <see cref="New" /> method delegates to <see cref="Guid.CreateVersion7()" /> from .NET.
///         The <see cref="NewForSqlServer" /> method applies byte shuffling to optimize for SQL Server
///         clustered index ordering (see <see href="https://github.com/dotnet/SqlClient/issues/2989" />).
///     </para>
/// </remarks>
public static class Guid7
{
    // Constants for version/variant validation
    private const byte Version7 = 0x70; // Version 7 in high nibble
    private const byte VariantRfc4122 = 0x80; // Variant 10 in high 2 bits

    /// <summary>
    ///     Generates a new UUID v7 using .NET's built-in implementation.
    /// </summary>
    /// <remarks>
    ///     Delegates to <see cref="Guid.CreateVersion7()" /> which provides RFC 9562 compliant
    ///     UUID v7 generation with proper timestamp and randomness. Use this for PostgreSQL,
    ///     MySQL, SQLite, and other databases that sort GUIDs by byte order.
    /// </remarks>
    /// <returns>A new UUID v7.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid New()
    {
        return Guid.CreateVersion7();
    }

    /// <summary>
    ///     Generates UUID v7 optimized for SQL Server clustered index ordering.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         SQL Server's UNIQUEIDENTIFIER type sorts GUIDs in a non-standard order:
    ///         bytes 10-15 first, then 8-9, then 6-7, then 0-5. This causes standard UUID v7
    ///         to have poor clustered index performance due to fragmentation (~35% larger indexes).
    ///     </para>
    ///     <para>
    ///         This method reorders the UUID v7 bytes so that the timestamp portion ends up in
    ///         the bytes SQL Server sorts first, ensuring proper chronological ordering.
    ///     </para>
    ///     <para>
    ///         See <see href="https://github.com/dotnet/SqlClient/issues/2989" /> for the upstream issue.
    ///     </para>
    /// </remarks>
    /// <returns>A new UUID v7 with SQL Server-optimized byte ordering.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Guid NewForSqlServer()
    {
        return ShuffleForSqlServer(Guid.CreateVersion7());
    }

    /// <summary>
    ///     Creates a UUID v7 from a specific timestamp.
    ///     Useful for testing, migration, or creating historical records.
    /// </summary>
    /// <param name="timestamp">The timestamp to embed in the UUID.</param>
    /// <returns>A new UUID v7 with the specified timestamp.</returns>
    public static Guid FromTimestamp(DateTimeOffset timestamp)
    {
        var unixMs = timestamp.ToUnixTimeMilliseconds();
        Span<byte> bytes = stackalloc byte[16];

        // Fill with random data first
        RandomNumberGenerator.Fill(bytes);

        // Set timestamp (big-endian, 48 bits into bytes 0–5)
        // Write as 64-bit big-endian first, then overwrite bytes 6+ with random data below
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)(unixMs);

        // Set version (7) and clear high nibble of byte 6
        bytes[6] = (byte)((bytes[6] & 0x0F) | Version7);

        // Set variant (RFC 4122)
        bytes[8] = (byte)((bytes[8] & 0x3F) | VariantRfc4122);

        return new Guid(bytes, true);
    }

    /// <summary>
    ///     Extracts the creation timestamp from a UUID v7.
    /// </summary>
    /// <param name="guid">The UUID to extract the timestamp from.</param>
    /// <returns>The embedded timestamp, or null if not a valid UUID v7.</returns>
    public static DateTimeOffset? GetTimestamp(Guid guid)
    {
        if (!IsVersion7(guid))
            return null;

        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, true, out _);

        // Read timestamp (big-endian, first 6 bytes = 48 bits)
        var timestamp = ((long)bytes[0] << 40)
                        | ((long)bytes[1] << 32)
                        | ((long)bytes[2] << 24)
                        | ((long)bytes[3] << 16)
                        | ((long)bytes[4] << 8)
                        | bytes[5];

        // A 48-bit value can exceed the DateTimeOffset range, and on a SQL-Server-optimized layout
        // (NewForSqlServer) the first 6 bytes are shuffled-out random data — so guard rather than throw
        // an ArgumentOutOfRangeException. NOTE: GetTimestamp is only meaningful for the STANDARD v7
        // layout; on a SQL-optimized value it returns garbage or null.
        if (timestamp < 0 || timestamp > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            return null;

        return DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
    }

    /// <summary>
    ///     Validates if the GUID is a UUID version 7.
    /// </summary>
    /// <param name="guid">The GUID to validate.</param>
    /// <returns>True if the GUID is a valid UUID v7, false otherwise.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVersion7(Guid guid)
    {
        Span<byte> bytes = stackalloc byte[16];
        guid.TryWriteBytes(bytes, true, out _);

        // Check version (byte 6, high nibble should be 0x70)
        var version = bytes[6] & 0xF0;

        // Check variant (byte 8, high 2 bits should be 10)
        var variant = bytes[8] & 0xC0;

        return version == Version7 && variant == VariantRfc4122;
    }

    /// <summary>
    ///     Shuffles a standard UUID v7 for SQL Server clustered index optimization.
    /// </summary>
    /// <param name="guid">A standard UUID v7.</param>
    /// <returns>The same UUID with bytes reordered for SQL Server sorting.</returns>
    public static Guid ShuffleForSqlServer(Guid guid)
    {
        // SQL Server UNIQUEIDENTIFIER sorting order:
        // Groups compared right-to-left: last 6 bytes, then bytes 8-9, then 6-7, then 0-5
        // To get chronological sorting, we need timestamp in the last 6 bytes
        //
        // Standard UUID v7: [ts(6)][ver+rand(2)][var+rand(8)]
        // SQL Server opt:   [rand(4)][rand(2)][ver+rand(2)][var+rand(2)][ts(6)]
        //
        Span<byte> standardBytes = stackalloc byte[16];
        guid.TryWriteBytes(standardBytes, true, out _);

        Span<byte> sqlBytes = stackalloc byte[16];

        // Move timestamp (bytes 0-5) to end (bytes 10-15) for SQL Server priority sorting
        sqlBytes[10] = standardBytes[0];
        sqlBytes[11] = standardBytes[1];
        sqlBytes[12] = standardBytes[2];
        sqlBytes[13] = standardBytes[3];
        sqlBytes[14] = standardBytes[4];
        sqlBytes[15] = standardBytes[5];

        // Version + random stay in bytes 6-7
        sqlBytes[6] = standardBytes[6];
        sqlBytes[7] = standardBytes[7];

        // Variant + part of random in bytes 8-9
        sqlBytes[8] = standardBytes[8];
        sqlBytes[9] = standardBytes[9];

        // Random data in bytes 0-5
        sqlBytes[0] = standardBytes[10];
        sqlBytes[1] = standardBytes[11];
        sqlBytes[2] = standardBytes[12];
        sqlBytes[3] = standardBytes[13];
        sqlBytes[4] = standardBytes[14];
        sqlBytes[5] = standardBytes[15];

        return new Guid(sqlBytes, true);
    }
}
