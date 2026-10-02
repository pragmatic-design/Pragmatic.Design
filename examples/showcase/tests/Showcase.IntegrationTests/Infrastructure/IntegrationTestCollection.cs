namespace Showcase.IntegrationTests.Infrastructure;

/// <summary>
///     xUnit collection that shares a single PostgreSQL container across all test classes.
///     Every test class decorated with [Collection(Name)] gets the same PostgresFixture instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
