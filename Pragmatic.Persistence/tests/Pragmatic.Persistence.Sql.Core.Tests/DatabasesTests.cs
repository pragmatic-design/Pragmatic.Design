using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The Docker databases' tests return early when the containers did not start, so the suite would look green on SQLite alone. This test
///     is the failure that keeps that from passing unseen. It used to fire only when PostgreSQL left logs, reading their absence as Docker's:
///     an image that does not pull leaves none either, and the suite was green (#365).
/// </summary>
[Collection("databases")]
public class DatabasesTests(TestDatabases databases)
{
    [Fact]
    public void The_docker_databases_started()
        => Assert.Null(databases.DockerUnavailable);
}
