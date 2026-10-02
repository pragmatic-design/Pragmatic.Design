namespace Conformance.Tests.Infrastructure;

/// <summary>
///     A single container for the whole suite: the case classes share it.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ConformanceTestCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
