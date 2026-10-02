using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     One container and one host for the whole suite: starting an ASP.NET host per test method is
///     what made the sibling suite pay ~446 ms of host per test before it was shared.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DistributedHostCollection : ICollectionFixture<DistributedHostFixture>
{
    public const string Name = "Distributed host";
}
