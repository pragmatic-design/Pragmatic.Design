// Pragmatic.Composition.HostWiring.Tests - Cache category discovery
// Asserts on the generated host, not on the reader: the category travels through a JSON payload, an
// assembly attribute and a template before it becomes a registration, and only the last of those is
// what a consumer gets.

using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     A cache category named by <c>[Cacheable(Category = typeof(T))]</c> must reach the host as
///     <c>cache.ForCategory&lt;T&gt;()</c>.
/// </summary>
/// <remarks>
///     <para>
///         Without that call the category is not a category: <c>CacheStackProvider.ForCategory&lt;T&gt;</c>
///         finds no keyed stack, falls back to the unkeyed one, and every entry the category was meant
///         to prefix lands in the same key namespace as everyone else's. The fallback is silent — the
///         cache still caches, so nothing fails, which is why the channel could be dead framework-wide
///         without a test noticing.
///     </para>
///     <para>
///         The assertion is on <c>Host.Services.g.cs</c> rather than on
///         <c>MetadataReader.ExtractCacheCategories</c> because a reader that returns the right list
///         into a template nobody calls reads correctly and registers nothing.
///     </para>
/// </remarks>
[Collection(HostWiringCollection.Name)]
public sealed class CacheCategoryDiscoveryTests(HostWiringFixture fixture)
{
    private const string HostServices = "Host.Services.g.cs";

    /// <summary>The registration the probe's category produces once it is discovered.</summary>
    private const string ExpectedRegistration =
        "cache.ForCategory<global::Probe.Domain.ProbeCacheCategory>(_ => { });";

    /// <summary>
    ///     The shape every Pragmatic app has: the category is declared in a referenced library, which
    ///     publishes it in its <c>[assembly: PragmaticMetadata(MetadataCategory.Caching, …)]</c>
    ///     payload, and the host reads it back.
    /// </summary>
    [Fact]
    public void Caching_CategoryDeclaredInAReferencedLibrary_IsRegisteredWithTheCachingBuilder()
    {
        var lines = HostWiringFixture.LinesOf(fixture.Control, HostServices);

        lines.Should().Contain(ExpectedRegistration,
            "a cache category published by a referenced assembly must be registered on the host's "
            + "CachingBuilder; without it ForCategory<T> resolves to the unkeyed stack and the "
            + "category's key prefix and TTL are silently dropped. Host.Services.g.cs has "
            + $"{lines.Count} lines and no such registration");
    }

    /// <summary>
    ///     A host that reaches <c>AddPragmaticCaching</c> without the builder overload has discovered
    ///     no categories at all. Pinned separately from the case above so that a fix which registers
    ///     the wrong category, rather than none, is not read as this one still being broken.
    /// </summary>
    [Fact]
    public void Caching_WhenCategoriesAreDiscovered_TheHostUsesTheBuilderOverload()
        => HostWiringFixture.LinesOf(fixture.Control, HostServices)
            .Should().NotContain("services.AddPragmaticCaching();",
                "the parameterless overload is the branch taken when no category was discovered; the "
                + "probe declares one, so taking it means discovery found nothing");

    /// <summary>
    ///     The same category declared in the host project itself reaches the builder identically.
    /// </summary>
    /// <remarks>
    ///     The route a library uses does not exist for the host: the
    ///     <c>[assembly: PragmaticMetadata]</c> is emitted by this same generator run, so
    ///     <c>MetadataReader.ReadFromReferences</c> — which walks <c>compilation.References</c> and
    ///     nothing else — never sees it. <c>CachingFeature</c> therefore hands the same document
    ///     <c>CachingMetadataTemplate</c> writes straight over through <c>HostLocalRegistration</c>,
    ///     in the payload shape rather than the entry-point shape, because the host renders
    ///     <c>ForCategory&lt;T&gt;</c> inline from the categories instead of calling a generated
    ///     <c>Add*</c>. Asserting against the control group, rather than against a literal, is what
    ///     stops a feature that quietly stopped generating from reading as repaired.
    /// </remarks>
    [Fact]
    public void Caching_CategoryDeclaredInTheHost_IsRegisteredJustAsFromALibrary()
        => WiringAssert.RegistrationReachesHost(fixture, "Caching", HostServices,
            line => line.Contains("ProbeCacheCategory", StringComparison.Ordinal));
}
