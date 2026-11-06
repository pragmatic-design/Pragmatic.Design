using Xunit;

namespace Showcase.Host.Sql.Tests;

/// <summary>One host and one PostgreSQL server for every test of the suite.</summary>
[CollectionDefinition(Name)]
public sealed class SqlHostCollection : ICollectionFixture<SqlHostFixture>
{
    public const string Name = "Showcase host on the Sql engine";
}
