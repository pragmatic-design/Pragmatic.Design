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
    private static object MakeAmenity(int keywordCount) => new
    {
        name = $"INV-{Guid.NewGuid():N}"[..18],
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

    [Fact]
    public async Task CreateAmenity_AtKeywordLimit_Succeeds()
    {
        var response = await PostAsync("/api/amenities", MakeAmenity(20));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
    }
}
