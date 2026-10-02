namespace TimeOff.IntegrationTests.Infrastructure;

/// <summary>One container for the whole suite; the test classes share it.</summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL";
}
