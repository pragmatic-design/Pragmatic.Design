using System.Buffers.Binary;
using System.Text;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>Rewrites the headers of one entry of a ZIP archive, producing packages no writer would.</summary>
internal static class ZipHeaders
{
    /// <summary>
    ///     Sets the "encrypted" bit of the general-purpose flag in the local and the central header of
    ///     <paramref name="name" />, leaving its data as it is: the archive then says the entry is encrypted with a method
    ///     it does not name.
    /// </summary>
    public static byte[] MarkEncrypted(byte[] zip, string name)
    {
        var copy = (byte[])zip.Clone();
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var patched = 0;

        for (var at = 0; at + 46 <= copy.Length; at++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(at));
            var nameAt = signature switch
            {
                0x04034b50 => at + 30, // local file header
                0x02014b50 => at + 46, // central directory header
                _ => -1,
            };
            if (nameAt < 0 || nameAt + nameBytes.Length > copy.Length) continue;

            var isLocal = signature == 0x04034b50;
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(isLocal ? at + 26 : at + 28));
            if (nameLength != nameBytes.Length || !copy.AsSpan(nameAt, nameLength).SequenceEqual(nameBytes)) continue;

            var flagAt = at + (isLocal ? 6 : 8);
            BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(flagAt),
                (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(flagAt)) | 0x0001));
            patched++;
        }

        if (patched != 2)
            throw new InvalidOperationException($"Expected a local and a central header for '{name}', patched {patched}.");
        return copy;
    }
}
