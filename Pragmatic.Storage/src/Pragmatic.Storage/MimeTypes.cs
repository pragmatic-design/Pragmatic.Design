namespace Pragmatic.Storage;

/// <summary>
///     Simple MIME type detection from file extension.
/// </summary>
/// <remarks>
///     The extension may include a leading dot (e.g. <c>.jpg</c>) or omit it (e.g. <c>jpg</c>);
///     both forms are accepted. Unrecognised or null extensions return
///     <c>application/octet-stream</c>.
/// </remarks>
public static class MimeTypes
{
    /// <summary>
    ///     Returns the MIME type for the given file extension.
    /// </summary>
    /// <param name="extension">
    ///     File extension with or without a leading dot (e.g. <c>.jpg</c> or <c>jpg</c>).
    ///     Null or empty returns <c>application/octet-stream</c>.
    /// </param>
    /// <returns>A MIME type string.</returns>
    public static string GetMimeType(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        // Normalise: ensure leading dot, then lower-case.
        var normalised = extension.StartsWith('.') ? extension.ToLowerInvariant() : ("." + extension).ToLowerInvariant();

        return normalised switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream",
        };
    }
}
