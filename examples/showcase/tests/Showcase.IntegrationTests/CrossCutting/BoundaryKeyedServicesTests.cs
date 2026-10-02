using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     An operation declares the two boundary-keyed services and they arrive — resolved, and working.
/// </summary>
/// <remarks>
///     <para>
///         <c>DbContext</c> and <c>IUnitOfWork</c> are registered with
///         <c>AddKeyedScoped(typeof(CatalogBoundary))</c>. The generated invoker asked for them without
///         the key, so no operation could declare either: the container refused at startup with a
///         message naming a generated type, and applications converged on a
///         <c>private IServiceProvider</c> field with <c>GetRequiredKeyedService</c> at every site —
///         a runtime lookup for a fact the generator holds.
///     </para>
///     <para>
///         ⚠️ The generator test asserts the attribute is emitted; it cannot tell whether the key
///         resolves to anything. That is what this case is for: the action reads a row through the
///         injected context, writes it, and saves through the injected unit of work, and the write is
///         read back over HTTP afterwards. An attribute naming a key nobody registered would pass the
///         generator test and fail here.
///     </para>
/// </remarks>
public class BoundaryKeyedServicesTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>Both services resolve, and what the operation saved is there on the next read.</summary>
    [Fact]
    public async Task TheInjectedContextAndUnitOfWork_ReadAndWrite()
    {
        var name = $"Keyed{Guid.NewGuid():N}"[..14];

        var created = await PostAsync<JsonElement>("/api/amenities", new { name, category = "Spa" });
        var id = created.GetProperty("id").GetGuid();

        var response = await PostAsync($"/api/amenities/{id}/touch", new { marker = "OK" });

        response.EnsureSuccessStatusCode();
        var count = await response.Content.ReadFromJsonAsync<int>(JsonOptions);
        count.Should().BeGreaterThan(0,
            "the count comes from the entity set of the injected DbContext, so a zero would mean it read nothing");

        var read = await GetAsync<JsonElement>($"/api/amenities/by-id/{id}");
        read.GetProperty("name").GetString().Should().Be($"{name} OK",
            "the operation saved through its own IUnitOfWork, and the row carries it");
    }
}
