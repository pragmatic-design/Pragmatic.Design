using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Metadata;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

public sealed class AssemblyMetadataProviderDefaultsTests
{
    // Returns a fixed list spanning two categories. Only GetMetadata() is overridden so the
    // GetMetadata(MetadataCategory) default interface method (filtering) is exercised.
    private sealed class StubMetadataProvider : IAssemblyMetadataProvider
    {
        public IReadOnlyList<AssemblyMetadataEntry> GetMetadata() =>
        [
            new(MetadataCategory.DI, "1", "{\"a\":1}"),
            new(MetadataCategory.Endpoints, "1", "{\"b\":2}"),
            new(MetadataCategory.DI, "1", "{\"c\":3}"),
        ];
    }

    [Fact]
    public void GetMetadata_ByCategory_ReturnsOnlyMatchingCategory()
    {
        IAssemblyMetadataProvider provider = new StubMetadataProvider();

        var di = provider.GetMetadata(MetadataCategory.DI);

        di.Should().HaveCount(2);
        di.Should().OnlyContain(e => e.Category == MetadataCategory.DI);
    }

    [Fact]
    public void GetMetadata_ByCategory_FiltersOutOtherCategories()
    {
        IAssemblyMetadataProvider provider = new StubMetadataProvider();

        var endpoints = provider.GetMetadata(MetadataCategory.Endpoints);

        endpoints.Should().ContainSingle()
            .Which.Category.Should().Be(MetadataCategory.Endpoints);
    }

    [Fact]
    public void GetMetadata_ByCategory_WithNoMatches_ReturnsEmpty()
    {
        IAssemblyMetadataProvider provider = new StubMetadataProvider();

        var jobs = provider.GetMetadata(MetadataCategory.Jobs);

        jobs.Should().BeEmpty();
    }
}
