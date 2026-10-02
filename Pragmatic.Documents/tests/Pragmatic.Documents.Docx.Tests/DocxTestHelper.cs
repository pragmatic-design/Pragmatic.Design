using System.IO.Compression;
using System.Xml.Linq;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>Utility to inspect DOCX ZIP contents.</summary>
internal static class DocxTestHelper
{
    internal static XDocument GetPart(byte[] docx, string partPath)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry(partPath) ?? throw new FileNotFoundException($"Part not found: {partPath}");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    internal static bool HasPart(byte[] docx, string partPath)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return zip.GetEntry(partPath) is not null;
    }

    internal static string[] GetEntryNames(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToArray();
    }

    internal static byte[]? GetMedia(byte[] docx, string mediaPath)
    {
        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry(mediaPath);
        if (entry is null) return null;
        using var stream = entry.Open();
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }
}
