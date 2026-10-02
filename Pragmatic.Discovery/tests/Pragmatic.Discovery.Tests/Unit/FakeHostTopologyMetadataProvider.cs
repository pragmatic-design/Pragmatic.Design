using Pragmatic.Composition.Metadata;

namespace Pragmatic.Discovery.Tests.Unit;

/// <summary>
/// Test double for <see cref="IAssemblyMetadataProvider"/> that yields a single
/// configurable metadata entry. Used to drive <c>HostTopologyInfo.FromRegistry()</c>
/// without depending on a real SG-generated provider.
/// </summary>
internal sealed class FakeHostTopologyMetadataProvider(MetadataCategory category, string jsonData)
    : IAssemblyMetadataProvider
{
    private readonly IReadOnlyList<AssemblyMetadataEntry> _entries =
        [new AssemblyMetadataEntry(category, "1.0.0", jsonData)];

    public IReadOnlyList<AssemblyMetadataEntry> GetMetadata() => _entries;
}
