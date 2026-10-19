using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Pragmatic.Documents.Xlsx.Tests;

/// <summary>Builds XLSX packages part by part, including ones no writer would produce.</summary>
internal static class XlsxPackages
{
    public const string Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Package(IEnumerable<KeyValuePair<string, string>> parts, CompressionLevel level = CompressionLevel.NoCompression)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in parts)
            {
                using var stream = zip.CreateEntry(name, level).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return output.ToArray();
    }

    public static string Workbook(string sheets)
        => $"<workbook xmlns=\"{Spreadsheet}\" xmlns:r=\"{Relationships}\"><sheets>{sheets}</sheets></workbook>";

    public static string WorkbookRelationships(string relationships)
        => $"<Relationships xmlns=\"{PackageRelationships}\">{relationships}</Relationships>";

    public static string Worksheet(string content) => $"<worksheet xmlns=\"{Spreadsheet}\">{content}</worksheet>";

    /// <summary>
    ///     Rewrites the uncompressed size the local and the central header of <paramref name="name" /> declare, leaving
    ///     its data as it is: the archive then says the entry is smaller than what it inflates to.
    /// </summary>
    public static byte[] DeclareUncompressedSize(byte[] zip, string name, uint size)
    {
        var copy = (byte[])zip.Clone();
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var patched = 0;

        for (var at = 0; at + 46 <= copy.Length; at++)
        {
            var signature = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(at));
            var (nameAt, sizeAt) = signature switch
            {
                0x04034b50 => (at + 30, at + 22), // local file header
                0x02014b50 => (at + 46, at + 24), // central directory header
                _ => (-1, -1),
            };
            if (nameAt < 0 || nameAt + nameBytes.Length > copy.Length) continue;

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(copy.AsSpan(signature == 0x04034b50 ? at + 26 : at + 28));
            if (nameLength != nameBytes.Length || !copy.AsSpan(nameAt, nameLength).SequenceEqual(nameBytes)) continue;

            BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(sizeAt), size);
            patched++;
        }

        if (patched != 2)
            throw new InvalidOperationException($"Expected a local and a central header for '{name}', patched {patched}.");
        return copy;
    }
}
