using System.Net;
using System.Net.Http.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     E2E tests for i18n integration: translation endpoint, culture switching, localized errors.
/// </summary>
public class InternationalizationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // ── Translation Endpoint ──

    [Fact]
    public async Task TranslationEndpoint_English_ReturnsErrorKeys()
    {
        var response = await GetRawAsync("/api/i18n/en");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TranslationResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Culture.Should().Be("en");
        body.Translations.Should().ContainKey("error.room.unavailable.detail");
        body.Translations.Should().ContainKey("error.room.unavailable.title");
        body.Translations["error.room.unavailable.title"].Should().Be("Room Unavailable");
    }

    [Fact]
    public async Task TranslationEndpoint_Italian_ReturnsLocalizedErrorKeys()
    {
        var response = await GetRawAsync("/api/i18n/it");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TranslationResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Culture.Should().Be("it");
        body.Translations.Should().ContainKey("error.room.unavailable.title");
        body.Translations["error.room.unavailable.title"].Should().Be("Camera Non Disponibile");
    }

    [Fact]
    public async Task TranslationEndpoint_PrefixFilter_ReturnsOnlyMatchingKeys()
    {
        var response = await GetRawAsync("/api/i18n/en?prefix=error.validation");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TranslationResponse>(JsonOptions);
        body.Should().NotBeNull();
        body!.Translations.Should().ContainKey("error.validation.error.detail");
        body.Translations.Should().ContainKey("error.validation.error.title");
        body.Translations.Should().NotContainKey("error.room.unavailable.detail");
    }

    [Fact]
    public async Task TranslationEndpoint_HasCacheControlHeader()
    {
        var response = await GetRawAsync("/api/i18n/en");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.MaxAge.Should().BeGreaterThan(TimeSpan.Zero);
    }

    // ── Response DTO ──

    private sealed record TranslationResponse(
        string Culture,
        Dictionary<string, string> Translations);
}
