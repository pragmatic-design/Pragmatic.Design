using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     W3 endpoint gap: [Idempotent] — header required (400 without), same key replays the
///     original response without re-executing, different key or different body re-executes.
///     The endpoint embeds an execution counter in the response to make replay observable.
/// </summary>
public class IdempotencyTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task MissingIdempotencyKey_Returns400()
    {
        var response = await PostAsync("/api/booking-tokens", new { purpose = "checkin" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Idempotency-Key");
    }

    [Fact]
    public async Task SameKeyAndBody_ReplaysOriginalResponse()
    {
        var key = Guid.NewGuid().ToString("N");

        var first = await PostWithKeyAsync(key, new { purpose = "same" });
        var second = await PostWithKeyAsync(key, new { purpose = "same" });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().Be(firstBody, "the retry must replay the captured response, not re-execute");
    }

    [Fact]
    public async Task DifferentKey_Reexecutes()
    {
        var first = await PostWithKeyAsync(Guid.NewGuid().ToString("N"), new { purpose = "diffkey" });
        var second = await PostWithKeyAsync(Guid.NewGuid().ToString("N"), new { purpose = "diffkey" });

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().NotBe(firstBody, "a new key is a new request");
    }

    [Fact]
    public async Task SameKeyDifferentBody_Reexecutes()
    {
        var key = Guid.NewGuid().ToString("N");

        var first = await PostWithKeyAsync(key, new { purpose = "payload-a" });
        var second = await PostWithKeyAsync(key, new { purpose = "payload-b" });

        var firstBody = await first.Content.ReadAsStringAsync();
        var secondBody = await second.Content.ReadAsStringAsync();
        secondBody.Should().NotBe(firstBody, "the body hash is part of the idempotency cache key");
    }

    private async Task<HttpResponseMessage> PostWithKeyAsync(string key, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/booking-tokens")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", key);
        return await Client.SendAsync(request);
    }
}
