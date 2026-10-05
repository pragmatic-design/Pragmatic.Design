using System.Text.Json;

namespace Pragmatic.Endpoints.Benchmarks.Documents;

/// <summary>
///     The public <c>simdjson-data</c> documents other JSON libraries publish numbers on, downloaded at
///     setup and cached outside the repository.
/// </summary>
/// <remarks>
///     <para>
///         Not committed: the repository states no licence ("provided for educational and testing
///         purposes; check individual files"), so the files are fetched from a pinned commit and kept in
///         the temp directory, never in the tree.
///     </para>
///     <para>
///         Pinned, so a number means the same input from one run to the next.
///     </para>
/// </remarks>
internal static class SimdJsonDocuments
{
    private const string Commit = "4197c425e857f0ec38e89822fdd0bd9ea21f4daf";

    private static readonly string CacheDirectory =
        Path.Combine(Path.GetTempPath(), "pragmatic-benchmarks", "simdjson-data", Commit);

    /// <summary>The document, deserialized into its model by STJ's defaults.</summary>
    /// <remarks>
    ///     Deserialized once, with reflection, outside anything timed: the benchmark is about writing a
    ///     graph the application already holds, as an endpoint does.
    /// </remarks>
    public static T Load<T>(string fileName)
    {
        var bytes = Download(fileName);
        return JsonSerializer.Deserialize<T>(bytes)
               ?? throw new InvalidOperationException($"{fileName} deserialized to null.");
    }

    private static byte[] Download(string fileName)
    {
        var path = Path.Combine(CacheDirectory, fileName);
        if (File.Exists(path))
            return File.ReadAllBytes(path);

        Directory.CreateDirectory(CacheDirectory);
        var url = $"https://raw.githubusercontent.com/simdjson/simdjson-data/{Commit}/jsonexamples/{fileName}";
        using var http = new HttpClient();
        var bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        File.WriteAllBytes(path, bytes);
        return bytes;
    }
}
