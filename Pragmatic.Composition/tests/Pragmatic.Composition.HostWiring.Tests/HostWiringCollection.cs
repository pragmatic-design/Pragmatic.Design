// Pragmatic.Composition.HostWiring.Tests - Collection definition

namespace Pragmatic.Composition.HostWiring.Tests;

/// <summary>
///     Shares the single <see cref="HostWiringFixture" /> across every class in this suite. Building
///     it costs three full compilations of the ecosystem; doing that per class would triple it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HostWiringCollection : ICollectionFixture<HostWiringFixture>
{
    public const string Name = "HostWiring";
}
