using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The Docker databases' tests skip when Docker is not there; when Docker answered and a container or its seed failed they would skip
///     too, and the suite would look green on SQLite alone. This test is the failure that keeps that from passing unseen.
/// </summary>
[Collection("databases")]
public class DatabasesTests(TestDatabases databases)
{
    [Fact]
    public void The_docker_databases_started_when_docker_answered()
        => Assert.False(databases.DockerAnswered && databases.DockerUnavailable is not null, databases.DockerUnavailable);
}
