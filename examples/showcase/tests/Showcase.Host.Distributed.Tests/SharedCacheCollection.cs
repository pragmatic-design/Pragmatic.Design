using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     Its own collection, and its own containers: the sibling fixture boots one host and has no
///     reason to pay for a Redis.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedCacheCollection : ICollectionFixture<SharedCacheFixture>
{
    public const string Name = "Distributed host, shared cache";
}
