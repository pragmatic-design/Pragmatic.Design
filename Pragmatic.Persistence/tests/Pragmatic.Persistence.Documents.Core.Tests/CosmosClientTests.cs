using System.Net;
using System.Text;
using System.Text.Json;
using Pragmatic.Persistence.Documents.Cosmos;
using Xunit;

namespace Pragmatic.Persistence.Documents.Core.Tests;

/// <summary>
///     The REST client: the master-key signature against the documentation's own example, then against the emulator: a database and a container,
///     documents in two partitions, a query in one partition with ORDER BY, a filter across partitions, and what the gateway answers to an
///     ORDER BY across partitions (the limit the documentation states).
/// </summary>
[Collection("cosmos")]
public class CosmosClientTests(CosmosServer cosmos)
{
    [Fact]
    public void The_signature_matches_the_documentation_example()
    {
        // learn.microsoft.com, Access Control on Azure Cosmos DB Resources, "Example Encoding".
        var key = Convert.FromBase64String("dsZQi3KtZmCv1ljt3VNWNm7sQUF1y5rJfC6kv5JiwvW0EndXdDku/dkKBp8/ufDToSxLzR4y+O/0H/t4bQtVNw==");
        Assert.Equal("c09PEVJrgp2uQRkr934kFbTqhByc7TVr3OHyqlu+c+c=", CosmosAuthorization.Signature("GET", "dbs", "dbs/ToDoList", "Thu, 27 Apr 2017 00:51:12 GMT", key));
        Assert.Equal("type%3dmaster%26ver%3d1.0%26sig%3dc09PEVJrgp2uQRkr934kFbTqhByc7TVr3OHyqlu%2bc%2bc%3d", CosmosAuthorization.Header("GET", "dbs", "dbs/ToDoList", "Thu, 27 Apr 2017 00:51:12 GMT", key), ignoreCase: true);
    }

    // Any start that failed, an image that did not pull too: the gate refuses to run this suite without Docker, so no failure here is Docker's
    // absence, and the tests below return early on it.
    [Fact]
    public void The_emulator_started()
        => Assert.Null(cosmos.Unavailable);

    [Fact]
    public async Task Documents_are_written_and_queried_in_a_partition_and_across()
    {
        if (cosmos.Unavailable is not null)
            return;
        var database = "probe" + Guid.NewGuid().ToString("N")[..8];
        await cosmos.Client.CreateDatabaseIfNotExistsAsync(database);
        await cosmos.Client.CreateContainerIfNotExistsAsync(database, "items", "/Owner");
        foreach (var (id, owner, n) in new[] { ("a", "x", 3), ("b", "x", 1), ("c", "x", 2), ("d", "y", 5) })
        {
            var body = Encoding.UTF8.GetBytes($"{{\"id\":\"{id}\",\"Owner\":\"{owner}\",\"N\":{n}}}");
            var created = await cosmos.Client.SendAsync(HttpMethod.Post, "docs", $"dbs/{database}/colls/items", $"dbs/{database}/colls/items/docs", body, h => h.TryAddWithoutValidation("x-ms-documentdb-partitionkey", $"[\"{owner}\"]"));
            Assert.Equal(HttpStatusCode.Created, created.Status);
            Assert.NotNull(created.ETag);
        }

        // One partition, ordered, paged: the gateway serves it.
        var inX = await Query(database, "SELECT c.id, c.N FROM c WHERE c.Owner = @o ORDER BY c.N DESC OFFSET 0 LIMIT 2", "x", "[\"x\"]");
        Assert.Equal(["a", "c"], inX.Select(d => d.GetProperty("id").GetString()));

        // Across partitions, a filter alone: served.
        var all = await Query(database, "SELECT c.id FROM c WHERE c.N > @o", 1, null);
        Assert.Equal(["a", "c", "d"], all.Select(d => d.GetProperty("id").GetString()).Order(StringComparer.Ordinal));

        // Across partitions with ORDER BY: what the gateway does with it, recorded (the documentation says it answers 400).
        var refused = await Record(() => Query(database, "SELECT c.id FROM c ORDER BY c.N", 0, null));
        Assert.True(refused is null or { Status: HttpStatusCode.BadRequest }, refused?.Message);
    }

    private async Task<List<JsonElement>> Query(string database, string text, object parameter, string? partitionKey)
    {
        var body = Encoding.UTF8.GetBytes($"{{\"query\":{JsonSerializer.Serialize(text, CosmosJson.Default.String)},\"parameters\":[{{\"name\":\"@o\",\"value\":{(parameter is string s ? JsonSerializer.Serialize(s, CosmosJson.Default.String) : parameter.ToString())}}}]}}");
        var rows = new List<JsonElement>();
        await foreach (var row in cosmos.Client.QueryAsync(database, "items", body, partitionKey, null))
            rows.Add(row);
        return rows;
    }

    private static async Task<CosmosException?> Record(Func<Task> query)
    {
        try
        {
            await query();
            return null;
        }
        catch (CosmosException e)
        {
            return e;
        }
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class CosmosJson : System.Text.Json.Serialization.JsonSerializerContext;
