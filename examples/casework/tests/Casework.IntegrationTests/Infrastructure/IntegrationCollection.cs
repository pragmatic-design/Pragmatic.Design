namespace Casework.IntegrationTests.Infrastructure;

/// <summary>
///     One PostgreSQL and one RabbitMQ for the whole suite; the test classes share both.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<RabbitMqFixture>
{
    public const string Name = "Casework";
}
