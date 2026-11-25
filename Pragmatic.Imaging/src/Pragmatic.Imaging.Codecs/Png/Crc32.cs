using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Pragmatic.Imaging.Codecs.Png;

/// <summary>
///     The CRC-32 of PNG chunks (polynomial 0xEDB88320, reflected), slicing-by-8: eight bytes per
///     step through eight tables. Incremental: a chunk's CRC covers its type and its data, which
///     arrive separately.
/// </summary>
internal static class Crc32
{
    private static readonly uint[][] Tables = BuildTables();

    /// <summary>The register before any byte: all ones.</summary>
    public const uint Initial = 0xFFFFFFFF;

    public static uint Append(uint register, ReadOnlySpan<byte> data)
    {
        var t0 = Tables[0];
        var t1 = Tables[1];
        var t2 = Tables[2];
        var t3 = Tables[3];
        var t4 = Tables[4];
        var t5 = Tables[5];
        var t6 = Tables[6];
        var t7 = Tables[7];

        var words = MemoryMarshal.Cast<byte, ulong>(data);
        foreach (var word in words)
        {
            var w = BitConverter.IsLittleEndian ? word : BinaryPrimitives.ReverseEndianness(word);
            var low = (uint)w ^ register;
            var high = (uint)(w >> 32);
            register = t7[low & 0xFF] ^ t6[(low >> 8) & 0xFF] ^ t5[(low >> 16) & 0xFF] ^ t4[low >> 24]
                ^ t3[high & 0xFF] ^ t2[(high >> 8) & 0xFF] ^ t1[(high >> 16) & 0xFF] ^ t0[high >> 24];
        }

        for (var i = words.Length * 8; i < data.Length; i++)
            register = t0[(register ^ data[i]) & 0xFF] ^ (register >> 8);
        return register;
    }

    /// <summary>The CRC value as PNG stores it: the register, complemented.</summary>
    public static uint Finish(uint register) => ~register;

    public static uint Compute(ReadOnlySpan<byte> data) => Finish(Append(Initial, data));

    private static uint[][] BuildTables()
    {
        var tables = new uint[8][];
        for (var t = 0; t < 8; t++)
            tables[t] = new uint[256];

        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            tables[0][n] = c;
        }

        for (var n = 0; n < 256; n++)
        {
            var c = tables[0][n];
            for (var t = 1; t < 8; t++)
            {
                c = tables[0][c & 0xFF] ^ (c >> 8);
                tables[t][n] = c;
            }
        }

        return tables;
    }
}
