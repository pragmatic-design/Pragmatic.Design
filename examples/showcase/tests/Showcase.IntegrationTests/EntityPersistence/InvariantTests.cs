using System.Net;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     E2E for the SG [Invariant] feature: Amenity.KeywordsWithinLimit() (max 20 keywords) is enforced
///     by the generated mutation invoker after apply, before persist. A violating create is rejected
///     and never reaches the database; a create at the limit succeeds.
/// </summary>
public class InvariantTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private static object MakeAmenity(int keywordCount, string? name = null) => new
    {
        name = name ?? $"INV-{Guid.NewGuid():N}"[..18],
        iconName = "wifi",
        keywords = Enumerable.Range(0, keywordCount).Select(i => $"k{i}").ToArray()
    };

    [Fact]
    public async Task CreateAmenity_ExceedingKeywordLimit_RejectedByInvariant()
    {
        var response = await PostAsync("/api/amenities", MakeAmenity(21));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "the [Invariant] KeywordsWithinLimit must reject >20 keywords before persist (InvariantViolationError = 422)");
    }

    /// <summary>
    ///     The refusal carries what the rule declares (#739): its code, the member it is about — as
    ///     <c>member</c> and as the key of <c>errors</c>, the way a property rule's failure is keyed — and
    ///     the sentence with the amenity's name in it.
    /// </summary>
    [Fact]
    public async Task CreateAmenity_ExceedingKeywordLimit_AnswersWithTheDeclaredCodeMemberAndMessage()
    {
        var name = $"INV-{Guid.NewGuid():N}"[..18];

        var response = await PostAsync("/api/amenities", MakeAmenity(21, name));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        root.GetProperty("code").GetString().Should().Be("AMENITY_TOO_MANY_KEYWORDS");
        root.GetProperty("member").GetString().Should().Be("keywords");
        root.GetProperty("errors").TryGetProperty("keywords", out _).Should().BeTrue();
        root.GetProperty("title").GetString().Should().Be($"The amenity {name} cannot have more than 20 keywords");
    }

    [Fact]
    public async Task CreateAmenity_AtKeywordLimit_Succeeds()
    {
        var response = await PostAsync("/api/amenities", MakeAmenity(20));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }
}
