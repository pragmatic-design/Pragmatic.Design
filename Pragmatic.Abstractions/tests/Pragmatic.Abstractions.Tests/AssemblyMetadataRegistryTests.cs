using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Metadata;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The registry is a process-global static — these tests use a dedicated marker payload
///     so they stay independent from providers registered by other tests.
/// </summary>
public sealed class AssemblyMetadataRegistryTests
{
    private sealed class StubProvider(params AssemblyMetadataEntry[] entries) : IAssemblyMetadataProvider
    {
        public IReadOnlyList<AssemblyMetadataEntry> GetMetadata() => entries;

        public IReadOnlyList<AssemblyMetadataEntry> GetMetadata(MetadataCategory category)
            => [.. entries.Where(e => e.Category == category)];
    }

    [Fact]
    public void Register_MakesProviderVisibleToQueries()
    {
        var marker = $"registry-test-{Guid.NewGuid():N}";
        var provider = new StubProvider(new AssemblyMetadataEntry(MetadataCategory.HostTopology, "1", marker));

        AssemblyMetadataRegistry.Register(provider);

        AssemblyMetadataRegistry.GetProviders().Should().Contain(provider);
        AssemblyMetadataRegistry.GetByCategory(MetadataCategory.HostTopology)
            .Should().Contain(e => e.JsonData == marker);
    }

    [Fact]
    public void FindByCategory_ReturnsFirstMatch_OrNullWhenAbsent()
    {
        var marker = $"registry-find-{Guid.NewGuid():N}";
        AssemblyMetadataRegistry.Register(
            new StubProvider(new AssemblyMetadataEntry(MetadataCategory.Translations, "1", marker)));

        AssemblyMetadataRegistry.FindByCategory(MetadataCategory.Translations).Should().NotBeNull();
        // A category no test registers: absent → null, no throw.
        AssemblyMetadataRegistry.FindByCategory(MetadataCategory.HealthChecks)
            .GetValueOrDefault().JsonData.Should().BeNull();
    }

    [Fact]
    public async Task Register_ConcurrentCalls_LoseNoProvider()
    {
        var providers = Enumerable.Range(0, 32)
            .Select(_ => new StubProvider())
            .ToArray();

        await Task.WhenAll(providers.Select(p => Task.Run(() => AssemblyMetadataRegistry.Register(p))));

        foreach (var provider in providers)
            AssemblyMetadataRegistry.GetProviders().Should().Contain(provider);
    }
}
