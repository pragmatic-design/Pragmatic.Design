using Google;
using Google.Apis.Requests;

namespace Pragmatic.Storage.GoogleCloud.Tests;

/// <summary>Shared helpers for the Google Cloud Storage provider tests.</summary>
internal static class GcsTestHelpers
{
    /// <summary>
    ///     Builds a <see cref="GoogleApiException" /> that represents a 404, matching how
    ///     <see cref="GoogleCloudFileStorage" /> detects a missing object (via <c>Error.Code</c>).
    /// </summary>
    public static GoogleApiException NotFound()
        => new("storage", "Not Found") { Error = new RequestError { Code = 404 } };
}
