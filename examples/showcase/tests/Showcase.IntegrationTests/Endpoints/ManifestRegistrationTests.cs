using Pragmatic.Testing.Assertions;
using Pragmatic.Endpoints.OpenApi;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     The host SG emits a [ModuleInitializer] that registers the aggregated manifest into
///     ManifestRegistry. Without it ManifestReader.ReadAll() is empty and runtime OpenAPI
///     enrichment silently does nothing.
/// </summary>
public class ManifestRegistrationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public void ManifestReader_AfterHostLoad_ReturnsPerModuleManifests()
    {
        // IntegrationTestBase boots the host (WebApplicationFactory), which loads the host
        // assembly and runs the generated module initializer.
        var docs = ManifestReader.ReadAll();

        docs.Should().NotBeEmpty("the host module initializer must register the aggregated manifest");

        // The aggregated form must be flattened into per-module documents with endpoints.
        docs.Should().OnlyContain(d => d.Modules == null, "aggregated wrappers must be flattened");
        docs.SelectMany(d => d.Endpoints ?? []).Should().NotBeEmpty();
    }

    /// <summary>
    ///     Each module manifest is read once, whoever registered it: a module can register its own and
    ///     the host embeds the same text in the aggregated one.
    /// </summary>
    [Fact]
    public void ManifestReader_ListsEachEndpointOnce()
    {
        var operations = ManifestReader.ReadAll()
            .SelectMany(d => d.Endpoints ?? [])
            .Select(e => $"{e.HttpMethod?.ToUpperInvariant()} {e.FullRoute}")
            .ToList();

        operations.Should().NotBeEmpty();
        operations.GroupBy(o => o).Where(g => g.Count() > 1).Select(g => g.Key).Should().BeEmpty();
    }
}
