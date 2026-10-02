namespace Pragmatic.Storage.Samples;

/// <summary>
/// Shared helpers for the storage samples: unique temp roots and cleanup.
/// </summary>
internal static class SampleSupport
{
    /// <summary>Creates a unique temporary directory for a sample run.</summary>
    public static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pragmatic-storage-demo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Removes the temporary directory created for a sample run.</summary>
    public static void Cleanup(string root)
    {
        if (!Directory.Exists(root))
            return;

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Transient file lock on Windows; ignore in a sample.
        }
    }
}
