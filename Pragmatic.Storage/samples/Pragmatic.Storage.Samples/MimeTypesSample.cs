namespace Pragmatic.Storage.Samples;

/// <summary>
/// Demonstrates <see cref="MimeTypes.GetMimeType"/>: extension-to-content-type mapping,
/// accepting names with or without a leading dot, and the octet-stream fallback.
/// </summary>
public static class MimeTypesSample
{
    public static void Run()
    {
        Console.WriteLine("--- MimeTypes.GetMimeType ---");

        string[] inputs = ["jpg", ".png", "PDF", ".json", "csv", "unknownext", "", null!];

        foreach (var ext in inputs)
        {
            var label = ext is null ? "<null>" : ext.Length == 0 ? "<empty>" : ext;
            Console.WriteLine($"  {label,-12} -> {MimeTypes.GetMimeType(ext)}");
        }

        // Typical usage: derive the content type from a file name's extension.
        const string fileName = "avatar.PNG";
        var contentType = MimeTypes.GetMimeType(Path.GetExtension(fileName));
        Console.WriteLine($"  content type for \"{fileName}\": {contentType}");

        Console.WriteLine();
    }
}
