using Xunit;

namespace Pragmatic.Composition.Tests.ControlPlane;

/// <summary>
///     The classes that register providers into <c>AssemblyMetadataRegistry</c>, run one at a time.
/// </summary>
/// <remarks>
///     ⚠️ The registry is a process-wide static and <b>append-only</b>: nothing can take a provider out
///     again, which is right for a registry filled by module initializers and means a test that adds one
///     changes what every other test in the assembly sees. xUnit runs different collections in parallel,
///     so this exists to keep that ordering out of the answers — the same reason
///     <c>TheProcessWideContractHostCollection</c> exists in <c>Pragmatic.Testing.Tests</c>.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TheProcessWideMetadataRegistryCollection
{
    public const string Name = "The process-wide metadata registry";
}
