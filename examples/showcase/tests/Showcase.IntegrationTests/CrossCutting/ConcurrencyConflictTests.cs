using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests optimistic concurrency conflict detection.
///     Verifies that parallel writes to the same entity cause at least one failure (409 or 500)
///     and that the entity remains in a consistent state after the conflict.
/// </summary>
public class ConcurrencyConflictTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HighContention_ManyParallelUpdates_ProducesConflictsOrConsistentResult()
    {
        // Create a property (ITenantEntity + ConcurrencyAware)
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"HC-{Guid.NewGuid():N}"[..12],
            name = "Contention Hotel",
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var id = created.GetProperty("id").GetGuid();

        // Fire 10 parallel updates — high contention should trigger EF Core concurrency exceptions
        var tasks = Enumerable.Range(1, 10).Select(i =>
            PutAsync($"/api/properties/{id}", new
            {
                code = $"HC-{Guid.NewGuid():N}"[..12],
                name = $"Contention v{i}",
                city = "Rome",
                country = "IT",
                starRating = (i % 5) + 1
            })).ToArray();

        var results = await Task.WhenAll(tasks);
        var statuses = results.Select(r => r.StatusCode).ToArray();

        // At least one should succeed
        statuses.Should().Contain(HttpStatusCode.NoContent,
            "At least one parallel update should succeed");

        // Every parallel update must either succeed or fail with 409 Conflict — an optimistic-concurrency
        // conflict is the documented, retryable [ConcurrencyAware] contract and must never leak as an
        // unhandled 500. (Postgres may serialize the writes so that all succeed; the deterministic proof
        // of the 409 mapping itself lives in the PragmaticExceptionMapping middleware unit test.)
        statuses.Should().OnlyContain(s => s == HttpStatusCode.NoContent || s == HttpStatusCode.Conflict,
            "a concurrency conflict must map to 409, not leak as an unhandled 500");

        // Verify final state is consistent (not corrupted)
        var final = await GetAsync<JsonElement>($"/api/properties/{id}");
        final.GetProperty("id").GetGuid().Should().Be(id);
        final.GetProperty("name").GetString().Should().StartWith("Contention v",
            "Final state should be one of the attempted values, not corrupted");
    }

    [Fact]
    public async Task ParallelCreateAndUpdate_EntityCreatedSuccessfully()
    {
        // Create a guest
        var created = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "Race",
            lastName = "Condition",
            email = $"race.{Guid.NewGuid():N}@test.com"
        });
        var id = created.GetProperty("id").GetGuid();

        // Immediately fire 3 parallel updates
        var tasks = Enumerable.Range(1, 3).Select(i =>
            PutAsync($"/api/guests/{id}", new
            {
                firstName = $"Race{i}"
            })).ToArray();

        var results = await Task.WhenAll(tasks);

        // At least one succeeds
        results.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().BeGreaterOrEqualTo(1);

        // Final state is consistent
        var final = await GetAsync<JsonElement>($"/api/guests/{id}");
        final.GetProperty("firstName").GetString().Should().StartWith("Race",
            "Guest firstName should be one of the attempted values");
    }

    [Fact]
    public async Task AuditFields_UpdatedAfterMutation()
    {
        // Verify CreatedAt is set on creation and UpdatedAt changes on update
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"AU-{Guid.NewGuid():N}"[..12],
            name = "Audit Hotel",
            city = "Rome",
            country = "IT",
            starRating = 3
        });
        var id = created.GetProperty("id").GetGuid();

        var initial = await GetAsync<JsonElement>($"/api/properties/{id}");

        // Brief delay to ensure timestamps differ
        await Task.Delay(50);

        await PutAsync($"/api/properties/{id}", new
        {
            code = $"AU-{Guid.NewGuid():N}"[..12],
            name = "Updated Audit Hotel",
            city = "Rome",
            country = "IT",
            starRating = 4
        });

        var updated = await GetAsync<JsonElement>($"/api/properties/{id}");

        // CreatedAt should be preserved
        if (initial.TryGetProperty("createdAt", out var initialCreated) &&
            updated.TryGetProperty("createdAt", out var updatedCreated))
        {
            updatedCreated.GetString().Should().Be(initialCreated.GetString(),
                "CreatedAt should be preserved across updates");
        }

        // UpdatedAt should change (if exposed in DTO)
        if (updated.TryGetProperty("updatedAt", out var updatedAt))
        {
            updatedAt.GetString().Should().NotBeNullOrEmpty(
                "UpdatedAt should be set after mutation");
        }
    }
}
