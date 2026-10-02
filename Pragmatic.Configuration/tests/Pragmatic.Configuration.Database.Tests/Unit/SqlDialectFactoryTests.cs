using Pragmatic.Testing.Assertions;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Tests.Unit;

public class SqlDialectFactoryTests
{
    [Theory]
    [InlineData(DatabaseProvider.PostgreSql, typeof(PostgresDialect))]
    [InlineData(DatabaseProvider.SqlServer, typeof(SqlServerDialect))]
    [InlineData(DatabaseProvider.Sqlite, typeof(SqliteDialect))]
    public void Create_ReturnsCorrectDialect(DatabaseProvider provider, Type expectedType)
    {
        var dialect = SqlDialectFactory.Create(provider);
        dialect.Should().BeOfType(expectedType);
    }

    [Fact]
    public void Create_InvalidProvider_Throws()
    {
        var act = () => SqlDialectFactory.Create((DatabaseProvider)999);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(DatabaseProvider.PostgreSql)]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.Sqlite)]
    public void AllDialects_HaveNonEmptyQueries(DatabaseProvider provider)
    {
        var dialect = SqlDialectFactory.Create(provider);

        dialect.GetValue.Should().NotBeNullOrWhiteSpace();
        dialect.SetValue.Should().NotBeNullOrWhiteSpace();
        dialect.DeleteValue.Should().NotBeNullOrWhiteSpace();
        dialect.GetSection.Should().NotBeNullOrWhiteSpace();
        dialect.GetSecret.Should().NotBeNullOrWhiteSpace();
        dialect.SetSecret.Should().NotBeNullOrWhiteSpace();
        dialect.GetChangesSince.Should().NotBeNullOrWhiteSpace();
        dialect.CreateSchema.Should().NotBeNullOrWhiteSpace();
    }
}
