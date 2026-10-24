using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.Persistence.Documents.Cosmos;

/// <summary>
///     The master-key authorization of a REST request (learn.microsoft.com, "Access Control on Azure Cosmos DB Resources"): HMAC-SHA256 under the
///     decoded key of <c>"{verb}\n{resourceType}\n{resourceLink}\n{date}\n\n"</c> (verb, type and date lowercase; the link as the resource names
///     are cased), Base64, in <c>type=master&amp;ver=1.0&amp;sig=…</c>, URL-encoded.
/// </summary>
public static class CosmosAuthorization
{
    /// <summary>The <c>x-ms-date</c> value: RFC 1123 ("R"), the same string signed.</summary>
    public static string Date(DateTimeOffset now) => now.UtcDateTime.ToString("R", CultureInfo.InvariantCulture);

    public static string Signature(string verb, string resourceType, string resourceLink, string date, byte[] key)
    {
        var payload = $"{verb.ToLowerInvariant()}\n{resourceType.ToLowerInvariant()}\n{resourceLink}\n{date.ToLowerInvariant()}\n\n";
        return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload)));
    }

    public static string Header(string verb, string resourceType, string resourceLink, string date, byte[] key)
        => WebUtility.UrlEncode($"type=master&ver=1.0&sig={Signature(verb, resourceType, resourceLink, date, key)}");
}
