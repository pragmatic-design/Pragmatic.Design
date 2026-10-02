using Pragmatic.Composition.Metadata;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Registers a single HostTopology metadata provider into the process-global
/// <see cref="AssemblyMetadataRegistry"/> exactly once.
/// <para>
/// The registry is append-only with no removal API, so mutation is global to the
/// test process. All tests that depend on (or are sensitive to) the registry's
/// HostTopology entry share this fixture via <see cref="HostTopologyRegistryCollection"/>
/// and run sequentially to keep behaviour deterministic.
/// </para>
/// </summary>
public sealed class HostTopologyRegistryFixture
{
    public const string HostName = "RegistryFixtureHost";

    public const string Json = $$"""
        {
            "host": "{{HostName}}",
            "includes": [
                { "module": "FixtureModule", "database": "FixtureDb", "provider": "InMemory" }
            ]
        }
        """;

    public HostTopologyRegistryFixture()
        => AssemblyMetadataRegistry.Register(
            new FakeHostTopologyMetadataProvider(MetadataCategory.HostTopology, Json));
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HostTopologyRegistryCollection : ICollectionFixture<HostTopologyRegistryFixture>
{
    public const string Name = "HostTopologyRegistry";
}
