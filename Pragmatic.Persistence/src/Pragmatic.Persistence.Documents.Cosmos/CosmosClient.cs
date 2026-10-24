using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pragmatic.Persistence.Documents.Cosmos;

/// <summary>A request the service refused: its HTTP status, sub-status and message.</summary>
public sealed class CosmosException(HttpStatusCode status, int subStatus, string message) : Exception($"Cosmos DB {(int)status} {status}{(subStatus != 0 ? "/" + subStatus : "")}: {message}")
{
    public HttpStatusCode Status { get; } = status;

    public int SubStatus { get; } = subStatus;

    /// <summary>409: the id (in its partition) is taken.</summary>
    public bool IsConflict => Status == HttpStatusCode.Conflict;

    /// <summary>412: the If-Match etag is not the document's any more.</summary>
    public bool IsPreconditionFailed => Status == HttpStatusCode.PreconditionFailed;
}

/// <summary>A response: status, the headers this client reads, the body.</summary>
public sealed class CosmosResponse(HttpStatusCode status, string? etag, string? continuation, byte[] body)
{
    public HttpStatusCode Status { get; } = status;

    public string? ETag { get; } = etag;

    public string? Continuation { get; } = continuation;

    public byte[] Body { get; } = body;
}

/// <summary>
///     Azure Cosmos DB's REST API over one <see cref="HttpClient" />: signed requests (<see cref="CosmosAuthorization" />), documents created,
///     upserted, read, replaced (If-Match) and deleted, queries paged by continuation. Connection string
///     <c>AccountEndpoint=https://…;AccountKey=…;</c>. Thread-safe as HttpClient is.
/// </summary>
public sealed class CosmosClient : IDisposable
{
    private const string Version = "2018-12-31";
    private readonly HttpClient _http;
    private readonly byte[] _key;

    public CosmosClient(string connectionString, bool allowInvalidCertificates = false)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        string? endpoint = null, key = null;
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0)
                continue;
            var name = part[..equals].Trim();
            if (name.Equals("AccountEndpoint", StringComparison.OrdinalIgnoreCase))
                endpoint = part[(equals + 1)..].Trim();
            else if (name.Equals("AccountKey", StringComparison.OrdinalIgnoreCase))
                key = part[(equals + 1)..].Trim(); // a Base64 key ends with '=': everything after the first '='
        }

        if (endpoint is null || key is null)
            throw new FormatException("A Cosmos DB connection string names AccountEndpoint and AccountKey.");
        _key = Convert.FromBase64String(key);
        var handler = new SocketsHttpHandler();
        if (allowInvalidCertificates)
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true; // the emulator's self-signed certificate, never in production
        _http = new HttpClient(handler) { BaseAddress = new Uri(endpoint.EndsWith('/') ? endpoint : endpoint + "/") };
    }

    /// <summary>
    ///     Sends a request: <paramref name="resourceType" /> and <paramref name="resourceLink" /> as the signature wants them (the link of the
    ///     resource, or of the parent for a create or a query), <paramref name="path" /> the URI's path. Statuses in <paramref name="accepted" />
    ///     come back as responses; any other failure throws <see cref="CosmosException" />.
    /// </summary>
    public async Task<CosmosResponse> SendAsync(HttpMethod method, string resourceType, string resourceLink, string path, byte[]? body, Action<HttpRequestHeaders>? headers, string contentType = "application/json", CancellationToken ct = default, params HttpStatusCode[] accepted)
    {
        using var request = new HttpRequestMessage(method, path);
        var date = CosmosAuthorization.Date(DateTimeOffset.UtcNow);
        request.Headers.TryAddWithoutValidation("authorization", CosmosAuthorization.Header(method.Method, resourceType, resourceLink, date, _key));
        request.Headers.TryAddWithoutValidation("x-ms-date", date);
        request.Headers.TryAddWithoutValidation("x-ms-version", Version);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        headers?.Invoke(request.Headers);
        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && !accepted.Contains(response.StatusCode))
            throw Failure(response, bytes);
        return new CosmosResponse(response.StatusCode, Header(response, "etag"), Header(response, "x-ms-continuation"), bytes);
    }

    /// <summary>
    ///     The container's partition key ranges (<c>GET …/pkranges</c>): what a query the gateway serves in one partition only (a COUNT) is sent to,
    ///     range by range, as the SDKs do.
    /// </summary>
    public async Task<List<string>> PartitionKeyRangesAsync(string database, string container, CancellationToken ct = default)
    {
        var link = $"dbs/{database}/colls/{container}";
        var response = await SendAsync(HttpMethod.Get, "pkranges", link, link + "/pkranges", null, null, ct: ct).ConfigureAwait(false);
        using var ranges = JsonDocument.Parse(response.Body);
        return ranges.RootElement.GetProperty("PartitionKeyRanges").EnumerateArray().Select(r => r.GetProperty("id").GetString()!).ToList();
    }

    /// <summary>The documents of a query, page after page (<c>x-ms-continuation</c>): one JSON document per page, its <c>Documents</c> array.</summary>
    public IAsyncEnumerable<JsonElement> QueryAsync(string database, string container, byte[] queryBody, string? partitionKey, int? maxItemCount, CancellationToken ct = default)
        => QueryAsync(database, container, queryBody, partitionKey, maxItemCount, null, ct);

    /// <summary>The same, in one partition key range (<c>x-ms-documentdb-partitionkeyrangeid</c>) when <paramref name="partitionKeyRange" /> names one.</summary>
    public async IAsyncEnumerable<JsonElement> QueryAsync(string database, string container, byte[] queryBody, string? partitionKey, int? maxItemCount, string? partitionKeyRange, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var link = $"dbs/{database}/colls/{container}";
        string? continuation = null;
        do
        {
            var current = continuation;
            var response = await SendAsync(HttpMethod.Post, "docs", link, link + "/docs", queryBody, h =>
            {
                h.TryAddWithoutValidation("x-ms-documentdb-isquery", "True");
                if (partitionKey is null)
                    h.TryAddWithoutValidation("x-ms-documentdb-query-enablecrosspartition", "True");
                else
                    h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", partitionKey);
                if (partitionKeyRange is not null)
                    h.TryAddWithoutValidation("x-ms-documentdb-partitionkeyrangeid", partitionKeyRange);
                if (maxItemCount is { } max)
                    h.TryAddWithoutValidation("x-ms-max-item-count", max.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (current is not null)
                    h.TryAddWithoutValidation("x-ms-continuation", current);
            }, "application/query+json", ct).ConfigureAwait(false);
            continuation = response.Continuation;
            using var page = JsonDocument.Parse(response.Body);
            foreach (var document in page.RootElement.GetProperty("Documents").EnumerateArray())
                yield return document.Clone();
        }
        while (continuation is not null);
    }

    public Task CreateDatabaseIfNotExistsAsync(string database, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, "dbs", "", "dbs", Encoding.UTF8.GetBytes($"{{\"id\":{JsonSerializerEscape(database)}}}"), null, ct: ct, accepted: HttpStatusCode.Conflict);

    /// <summary>A container partitioned by <paramref name="partitionKeyPath" /> (<c>/CustomerId</c>), with the composite indexes an ORDER BY on several keys needs (each: the paths and their orders).</summary>
    public Task CreateContainerIfNotExistsAsync(string database, string container, string partitionKeyPath, IEnumerable<(string Path, bool Descending)[]>? compositeIndexes = null, CancellationToken ct = default)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("id", container);
            w.WriteStartObject("partitionKey");
            w.WriteStartArray("paths");
            w.WriteStringValue(partitionKeyPath);
            w.WriteEndArray();
            w.WriteString("kind", "Hash");
            w.WriteNumber("version", 2);
            w.WriteEndObject();
            if (compositeIndexes is not null)
            {
                w.WriteStartObject("indexingPolicy");
                w.WriteString("indexingMode", "consistent");
                w.WriteBoolean("automatic", true);
                w.WriteStartArray("includedPaths");
                w.WriteStartObject();
                w.WriteString("path", "/*");
                w.WriteEndObject();
                w.WriteEndArray();
                w.WriteStartArray("compositeIndexes");
                foreach (var index in compositeIndexes)
                {
                    w.WriteStartArray();
                    foreach (var (path, descending) in index)
                    {
                        w.WriteStartObject();
                        w.WriteString("path", path);
                        w.WriteString("order", descending ? "descending" : "ascending");
                        w.WriteEndObject();
                    }

                    w.WriteEndArray();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return SendAsync(HttpMethod.Post, "colls", $"dbs/{database}", $"dbs/{database}/colls", stream.ToArray(), null, ct: ct, accepted: HttpStatusCode.Conflict);
    }

    private static string JsonSerializerEscape(string text)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
            w.WriteStringValue(text);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : response.Content.Headers.TryGetValues(name, out var content) ? content.FirstOrDefault() : null;

    private static CosmosException Failure(HttpResponseMessage response, byte[] body)
    {
        var subStatus = int.TryParse(Header(response, "x-ms-substatus"), out var sub) ? sub : 0;
        var message = body.Length == 0 ? response.ReasonPhrase ?? "" : Encoding.UTF8.GetString(body);
        if (message.Length > 600)
            message = message[..600] + "…";
        return new CosmosException(response.StatusCode, subStatus, message);
    }

    public void Dispose() => _http.Dispose();
}
