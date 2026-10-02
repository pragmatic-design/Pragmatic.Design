using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Conformance.Tests.Infrastructure;

/// <summary>
///     Base of the E2E cases: an HTTP client against the real app.
/// </summary>
/// <remarks>
///     ⚠️ No permission to configure: the conformance operations are <c>[AllowAnonymous]</c> because
///     authorization is a cell of its own in the matrix. Tying it to every case would make every failure
///     ambiguous — a missing permission, or a defect of the shape under test?
/// </remarks>
[Collection(ConformanceTestCollection.Name)]
[Trait("Category", "Conformance")]
public abstract class E2ETestBase(PostgresFixture fixture) : IAsyncLifetime
{
    private ConformanceWebFactory _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected PostgresFixture Fixture { get; } = fixture;

    protected IServiceProvider Services => _factory.Services;

    protected static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public Task InitializeAsync()
    {
        _factory = new ConformanceWebFactory(Fixture.ConnectionString);
        Client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     The response body as JSON.
    /// </summary>
    /// <remarks>
    ///     On an unsuccessful response it throws with the body in the message: a test that fails saying only
    ///     «500» forces a rerun to find out why.
    /// </remarks>
    protected static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    protected Task<HttpResponseMessage> PostAsync(string url, object body)
        => Client.PostAsJsonAsync(url, body, JsonOptions);

    protected Task<HttpResponseMessage> PutAsync(string url, object body)
        => Client.PutAsJsonAsync(url, body, JsonOptions);

    /// <summary>PATCH, the verb a <c>[Patch&lt;T&gt;]</c> arrives with.</summary>
    protected Task<HttpResponseMessage> PatchAsync(string url, object body)
        => Client.PatchAsJsonAsync(url, body, JsonOptions);
}
