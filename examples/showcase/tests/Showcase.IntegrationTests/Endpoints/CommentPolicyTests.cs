using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Comments;
using Pragmatic.Result;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     E2E proof that a consumer-registered <see cref="ICommentPolicy{TEntityId}"/> is actually invoked
///     by the SG-generated comment actions.
///     <para>
///     This is the detector for a defect that would ship silently: generated code that resolved the
///     policy from <c>((IInfrastructure&lt;IServiceProvider&gt;)_db).Instance</c> would be asking EF
///     Core's <b>internal</b> service provider. A policy registered the way the README documents
///     (<c>services.AddScoped&lt;ICommentPolicy&lt;Guid&gt;, …&gt;()</c>) never lands there, so the
///     lookup would return <c>null</c> and every hook would be skipped without an error or a log line.
///     </para>
///     <para>
///     The policy below rejects one specific content and allows everything else, so a regression cannot
///     hide: if the hook stops firing, the rejected POST starts succeeding.
///     </para>
/// </summary>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CommentPolicyTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string BannedContent = "buy cheap watches";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private ShowcaseWebFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new ShowcaseWebFactory(
            fixture,
            extraServices: services => services.AddScoped<ICommentPolicy<Guid>, SpamRejectingCommentPolicy>());

        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", "test-tenant");
        _client.DefaultRequestHeaders.Add("X-User-Id", "test-user");
        _client.DefaultRequestHeaders.Add("X-User-Name", "Comment Policy Test");
        _client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*,booking.*");

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task AddComment_WhenPolicyRejectsTheContent_IsRefused()
    {
        var reservationId = await CreateReservation();

        var response = await _client.PostAsJsonAsync(
            CommentsUrl(reservationId), new { content = BannedContent }, JsonOptions);

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().NotBe(HttpStatusCode.Created,
            "the registered ICommentPolicy rejects this content — a 201 means the hook was never called");

        // The policy's own CommentRejectedError reaches the caller: 422 with the reason it gave.
        // A bare 403 would tell the caller nothing about why.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "body was: {0}", body);
        body.Should().Contain("COMMENT_REJECTED").And.Contain("Content looks like spam.");
    }

    [Fact]
    public async Task AddComment_WhenPolicyAllowsTheContent_Succeeds()
    {
        var reservationId = await CreateReservation();

        var response = await _client.PostAsJsonAsync(
            CommentsUrl(reservationId), new { content = "Lovely stay, thank you." }, JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.Created,
            "the policy only rejects the banned content — everything else must still go through");
    }

    [Fact]
    public async Task AddComment_WhenPolicyRejects_NothingIsPersisted()
    {
        var reservationId = await CreateReservation();

        await _client.PostAsJsonAsync(CommentsUrl(reservationId), new { content = BannedContent }, JsonOptions);

        var list = await _client.GetFromJsonAsync<JsonElement>(CommentsUrl(reservationId), JsonOptions);
        list.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string CommentsUrl(Guid reservationId) =>
        $"/api/booking/reservations/{reservationId}/comments";

    private async Task<Guid> CreateReservation()
    {
        var guestBody = new { firstName = "CP", lastName = "Guest", email = $"cp.{Guid.NewGuid():N}@test.com" };
        var guest = await PostJson("/api/guests", guestBody);
        var guestId = guest.GetProperty("id").GetGuid();

        var propBody = new { code = $"CP-{Guid.NewGuid():N}"[..12], name = "PolicyProp", city = "Rome", country = "IT", starRating = 3 };
        var property = await PostJson("/api/properties", propBody);
        var propertyId = property.GetProperty("id").GetGuid();

        var rtBody = new { propertyId, name = "CP Room", code = "CPR", baseRate = 100m, totalRooms = 5 };
        var roomType = await PostJson("/api/room-types", rtBody);
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var reservationBody = new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(30).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(33).ToString("O"),
                numberOfGuests = 2
            }
        };

        var response = await _client.PostAsJsonAsync("/api/reservations?api-version=1.0", reservationBody, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Guid>(JsonOptions);
    }

    private async Task<JsonElement> PostJson(string url, object body)
    {
        var response = await _client.PostAsJsonAsync(url, body, JsonOptions);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
    }
}

/// <summary>Rejects one known-bad content and allows everything else.</summary>
internal sealed class SpamRejectingCommentPolicy : ICommentPolicy<Guid>
{
    public Task<VoidResult<CommentRejectedError>> CanAddAsync(
        Guid parentId, string content, string? authorId, CancellationToken ct = default)
        => Task.FromResult(content.Contains("buy cheap watches", StringComparison.OrdinalIgnoreCase)
            ? VoidResult<CommentRejectedError>.Failure(CommentRejectedError.Because("Content looks like spam."))
            : VoidResult<CommentRejectedError>.Success());
}
