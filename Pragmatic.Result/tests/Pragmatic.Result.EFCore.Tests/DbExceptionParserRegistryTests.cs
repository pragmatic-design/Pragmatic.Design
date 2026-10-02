using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Result.EntityFrameworkCore.PostgreSQL;
using Pragmatic.Result.EntityFrameworkCore.SqlServer;
using Xunit;

namespace Pragmatic.Result.EntityFrameworkCore.Tests;

public class DbExceptionParserRegistryTests
{
    [Fact]
    public void TwoProviders_Registered_RegistryContainsBothParsersPlusHeuristic()
    {
        var services = new ServiceCollection();

        services.AddSqlServerResultErrorHandling();
        services.AddPostgreSqlResultErrorHandling();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DbExceptionParserRegistry>();

        registry.RegisteredProviders.Should().Contain(["SqlServer", "PostgreSQL", "Heuristic"]);
    }

    [Fact]
    public void TwoProviders_HeuristicIsAlwaysLast()
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlResultErrorHandling();
        services.AddSqlServerResultErrorHandling();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DbExceptionParserRegistry>();

        registry.RegisteredProviders[^1].Should().Be("Heuristic");
    }

    [Fact]
    public void SameProvider_RegisteredTwice_ParserNotDuplicated()
    {
        var services = new ServiceCollection();
        services.AddSqlServerResultErrorHandling();
        services.AddSqlServerResultErrorHandling();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DbExceptionParserRegistry>();

        registry.RegisteredProviders.Count(p => p == "SqlServer").Should().Be(1);
    }

    [Fact]
    public void Parse_WithBothProviders_PicksPostgresParserForPostgresException()
    {
        var services = new ServiceCollection();
        services.AddSqlServerResultErrorHandling();
        services.AddPostgreSqlResultErrorHandling();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<DbExceptionParserRegistry>();

        // A native PostgreSQL unique-violation (SQLSTATE 23505). The SqlServer parser cannot handle it
        // (CanParse == false), so the PostgreSQL parser — which survives the multi-provider
        // registration — must classify it.
        var pgException = new Npgsql.PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            "23505");

        var info = registry.Parse(pgException);

        info.ErrorType.Should().Be(DbErrorType.UniqueConstraint);
        info.SqlState.Should().Be("23505");
    }

    [Fact]
    public void Default_UsesHeuristicOnly()
    {
        DbExceptionParserRegistry.Default.RegisteredProviders.Should().Equal("Heuristic");
    }

    [Fact]
    public void Parse_UnclassifiableException_ReturnsUnknownWithDetails()
    {
        var registry = DbExceptionParserRegistry.Default;

        var info = registry.Parse(new InvalidOperationException("something odd"));

        info.ErrorType.Should().Be(DbErrorType.Unknown);
        info.Details.Should().Contain("something odd");
    }

    [Fact]
    public void Parse_ProviderReturnsUnknown_FallsThroughToHeuristic()
    {
        // A provider parser that recognizes the exception but cannot classify the code (Unknown) must
        // not shadow the heuristic, which can still read the message.
        var registry = new DbExceptionParserRegistry([new UnknownProviderParser()]);

        var info = registry.Parse(new InvalidOperationException("UNIQUE constraint failed: Users.Email"));

        info.ErrorType.Should().Be(DbErrorType.UniqueConstraint);
    }

    [Fact]
    public void Ctor_NullParsers_Throws()
    {
        var act = () => new DbExceptionParserRegistry(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private sealed class UnknownProviderParser : IDbExceptionParser
    {
        public string ProviderName => "AlwaysUnknown";
        public bool CanParse(Exception exception) => true;
        public DbErrorInfo? Parse(Exception exception)
            => new DbErrorInfo { ErrorType = DbErrorType.Unknown, Details = exception.Message };
    }
}
