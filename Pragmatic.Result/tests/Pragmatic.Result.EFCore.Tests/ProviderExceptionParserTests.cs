using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Npgsql;
using Pragmatic.Result.EntityFrameworkCore.MySql;
using Pragmatic.Result.EntityFrameworkCore.PostgreSQL;
using Pragmatic.Result.EntityFrameworkCore.SqlServer;
using Pragmatic.Result.EntityFrameworkCore.Sqlite;
using Xunit;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

public class ProviderExceptionParserTests
{
    // ----- SqlServer (native SqlException not constructible without a server) -----

    [Fact]
    public void SqlServer_ProviderName_IsSqlServer()
        => SqlServerExceptionParser.Instance.ProviderName.Should().Be("SqlServer");

    [Fact]
    public void SqlServer_CanParse_NonSqlException_IsFalse()
        => SqlServerExceptionParser.Instance.CanParse(new InvalidOperationException("nope")).Should().BeFalse();

    [Fact]
    public void SqlServer_Parse_NonSqlException_ReturnsNull()
        => SqlServerExceptionParser.Instance.Parse(new InvalidOperationException("nope")).Should().BeNull();

    // ----- MySql (native MySqlException not constructible without a server) -----

    [Fact]
    public void MySql_ProviderName_IsMySql()
        => MySqlExceptionParser.Instance.ProviderName.Should().Be("MySql");

    [Fact]
    public void MySql_CanParse_NonMySqlException_IsFalse()
        => MySqlExceptionParser.Instance.CanParse(new InvalidOperationException("nope")).Should().BeFalse();

    // ----- PostgreSQL (native PostgresException is constructible) -----

    [Fact]
    public void Postgres_ProviderName_IsPostgreSql()
        => PostgreSqlExceptionParser.Instance.ProviderName.Should().Be("PostgreSQL");

    [Theory]
    [InlineData("23505", DbErrorType.UniqueConstraint)]
    [InlineData("23503", DbErrorType.ForeignKeyConstraint)]
    [InlineData("23502", DbErrorType.NullConstraint)]
    [InlineData("23514", DbErrorType.CheckConstraint)]
    [InlineData("22003", DbErrorType.NumericOverflow)]
    [InlineData("40P01", DbErrorType.Deadlock)]
    [InlineData("08006", DbErrorType.ConnectionFailure)]
    public void Postgres_Parse_KnownSqlState_MapsToErrorType(string sqlState, DbErrorType expected)
    {
        var ex = new PostgresException("boom", "ERROR", "ERROR", sqlState);

        var info = PostgreSqlExceptionParser.Instance.Parse(ex);

        info.Should().NotBeNull();
        info!.Value.ErrorType.Should().Be(expected);
        info.Value.SqlState.Should().Be(sqlState);
    }

    [Fact]
    public void Postgres_Parse_WrappedInInnerException_IsUnwrapped()
    {
        var pg = new PostgresException("boom", "ERROR", "ERROR", "23505");
        var wrapped = new InvalidOperationException("outer", pg);

        var info = PostgreSqlExceptionParser.Instance.Parse(wrapped);

        info!.Value.ErrorType.Should().Be(DbErrorType.UniqueConstraint);
    }

    // ----- SQLite (native SqliteException is constructible) -----

    [Fact]
    public void Sqlite_ProviderName_IsSqlite()
        => SqliteExceptionParser.Instance.ProviderName.Should().Be("Sqlite");

    [Fact]
    public void Sqlite_Parse_UniqueConstraintMessage_MapsToUnique()
    {
        // Primary code 19 = SQLITE_CONSTRAINT; classification then reads the descriptive message.
        var ex = new SqliteException("UNIQUE constraint failed: Users.Email", 19);

        var info = SqliteExceptionParser.Instance.Parse(ex);

        info!.Value.ErrorType.Should().Be(DbErrorType.UniqueConstraint);
        info.Value.TableName.Should().Be("Users");
        info.Value.ColumnName.Should().Be("Email");
    }

    [Fact]
    public void Sqlite_Parse_NotNullConstraintMessage_MapsToNull()
    {
        var ex = new SqliteException("NOT NULL constraint failed: Users.Name", 19);

        var info = SqliteExceptionParser.Instance.Parse(ex);

        info!.Value.ErrorType.Should().Be(DbErrorType.NullConstraint);
        info.Value.ColumnName.Should().Be("Name");
    }

    [Fact]
    public void Sqlite_CanParse_NonSqliteException_IsFalse()
        => SqliteExceptionParser.Instance.CanParse(new InvalidOperationException("nope")).Should().BeFalse();
}
