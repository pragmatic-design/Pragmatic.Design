namespace Pragmatic.Imaging.Codecs.Tests.WebP;

/// <summary>The WebP files libwebp wrote (<c>References/WebP</c>, with their provenance in its manifest).</summary>
internal static class WebPFiles
{
    public static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "References", "WebP");

    public static TheoryData<string> All()
    {
        var names = new TheoryData<string>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*.webp").Order(StringComparer.Ordinal))
            names.Add(Path.GetFileName(path));
        return names;
    }

    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(Directory, name));
}
