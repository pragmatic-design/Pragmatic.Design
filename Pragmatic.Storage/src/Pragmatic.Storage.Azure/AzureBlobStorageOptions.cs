namespace Pragmatic.Storage.Azure;

/// <summary>
///     Configuration for Azure Blob Storage.
/// </summary>
public sealed class AzureBlobStorageOptions
{
    /// <summary>
    ///     Prefix for blob container names (e.g. "myapp-" → "myapp-photos").
    ///     Default: empty (container name = logical container name).
    /// </summary>
    public string ContainerPrefix { get; set; } = "";

    /// <summary>
    ///     Maximum accepted size of a single uploaded file, in bytes.
    ///     <c>0</c> (default) means no limit. Set a positive value to reject oversized
    ///     uploads before streaming to Azure, preventing unexpected storage and transfer costs.
    /// </summary>
    public long MaxFileSizeBytes { get; init; }
}
