using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The C# literal of a <c>[DefaultValue]</c> translated into the SQL literal the DDL requires.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ One value, two languages. <c>EntityTransform.ToLiteral</c> produces what <em>compiles</em>
///         — what the generated factory assigns — and the column interpolates its default raw. In
///         PostgreSQL <c>"pending"</c> in double quotes is an <b>identifier</b>: the migration would be
///         refused with <c>0A000: cannot use column reference in DEFAULT expression</c>, and the
///         application would not start.
///     </para>
///     <para>
///         A numeric <c>[DefaultValue]</c> hides the difference, because there the two literals happen
///         to coincide — while the example documented on the attribute is <c>[DefaultValue("EUR")]</c>,
///         exactly the shape that breaks.
///     </para>
/// </remarks>
public class SqlDefaultMapperTests
{
    /// <summary>⚠️ The breaking case: a string comes out quoted the SQL way.</summary>
    [Fact]
    public void AString_IsQuotedTheWaySqlQuotes()
    {
        SqlDefaultMapper.ToSqlLiteral("\"pending\"", EfCoreProvider.PostgreSql)
            .Should().Be("'pending'",
                "double quotes in PostgreSQL are an identifier, not a string");
    }

    /// <summary>An apostrophe inside the value is doubled, or it closes the string halfway.</summary>
    [Fact]
    public void AQuoteInsideTheValue_IsDoubled()
    {
        SqlDefaultMapper.ToSqlLiteral("\"it's\"", EfCoreProvider.PostgreSql)
            .Should().Be("'it''s'");
    }

    /// <summary>
    ///     The boolean is the only value without a neutral spelling: it depends on the provider.
    /// </summary>
    /// <remarks>
    ///     That is why this mapper takes the provider, exactly like <c>SqlTypeMapper</c>. Passed through
    ///     as a C# literal, <c>true</c> would work on PostgreSQL and be refused on SQL Server — the same
    ///     defect, latent.
    /// </remarks>
    [Fact]
    public void ABoolean_IsWrittenInTheProvidersDialect()
    {
        SqlDefaultMapper.ToSqlLiteral("true", EfCoreProvider.PostgreSql).Should().Be("TRUE");
        SqlDefaultMapper.ToSqlLiteral("false", EfCoreProvider.PostgreSql).Should().Be("FALSE");
        SqlDefaultMapper.ToSqlLiteral("true", EfCoreProvider.SqlServer).Should().Be("1");
        SqlDefaultMapper.ToSqlLiteral("false", EfCoreProvider.Sqlite).Should().Be("0");
    }

    /// <summary>A number is spelled the same in both languages: the coincidence that hides the rest.</summary>
    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void ANumber_PassesThrough(string literal)
    {
        SqlDefaultMapper.ToSqlLiteral(literal, EfCoreProvider.PostgreSql).Should().Be(literal);
    }

    /// <summary>An enum arrives as a cast on its number, and the column holds the number.</summary>
    [Fact]
    public void AnEnum_LosesItsCast()
    {
        SqlDefaultMapper.ToSqlLiteral("(global::TestApp.Colour)2", EfCoreProvider.PostgreSql)
            .Should().Be("2", "a user enum is stored in its column as an integer");
    }

    /// <summary>
    ///     ⚠️ An unknown shape does not become an invented default.
    /// </summary>
    /// <remarks>
    ///     The declared choice: a column without a default is predictable, an unquoted word is a refused
    ///     migration. Emitting «something» would be exactly the defect.
    /// </remarks>
    [Theory]
    [InlineData("SomeIdentifier")]
    [InlineData("global::Ns.Something.Member")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownShape_ProducesNoDefault(string? literal)
    {
        SqlDefaultMapper.ToSqlLiteral(literal, EfCoreProvider.PostgreSql).Should().BeNull();
    }
}
